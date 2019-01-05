using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Net;
using System.Configuration;
using System.Linq;
using FluidDynamics.DynamicsCRMUtilities;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Sdk.Metadata;

namespace FluidDynamics
{
	public interface IExtendedOrganizationService : IOrganizationService
	{
		string EnvironmentName { get; }
		List<Entity> RetrieveAllRecords(string fetchXML);
		IEnumerable<OneToManyRelationshipMetadata> GetOneToManyRelationships(string entityName);
		IExecuteMultipleResponseAdapter ExecuteMultipleReturnAdapter(ExecuteMultipleRequest executeMultipleRequest);
	}

	public class ExtendedOrganizationService : IExtendedOrganizationService
	{
		private static readonly log4net.ILog Log =
			log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()
				.DeclaringType);

		private readonly IOrganizationService _crmService;
		private readonly bool _readOnly;
		public string EnvironmentName { get; }

		private ExtendedOrganizationService(IOrganizationService crmService, string environmentName, bool readOnly)
		{
			_crmService = crmService;
			EnvironmentName = environmentName;
			_readOnly = readOnly;
		}

		/// <summary>
		/// Creates a new ExtendedOrganizationService from an existing IOrganizationService
		/// </summary>
		/// <param name="crmService">The IOrganizationService to use to connect to Dynamics</param>
		/// <param name="environmentName">The friendly name of the environment that is being connected to e.g. DEV, TEST, PROD</param>
		/// <param name="readOnly">If true, any calls to Dynamics which modify data will only be simulated.</param>
		public static ExtendedOrganizationService FromIOrganizationService(IOrganizationService crmService, string environmentName,
			bool readOnly)
		{
			return new ExtendedOrganizationService(crmService, environmentName, readOnly);
		}

		/// <summary>
		/// Creates a new ExtendedOrganizationService from a Dynamics connection string
		/// </summary>
		/// <param name="connectionString">The connection string to use to connect to Dynamics</param>
		/// <param name="environmentName">The friendly name of the environment that is being connected to e.g. DEV, TEST, PROD</param>
		/// <param name="readOnly">If true, any calls to Dynamics which modify data will only be simulated.</param>
		public static ExtendedOrganizationService FromConnectionString(string connectionString, string environmentName, bool readOnly)
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
			var conn = new CrmServiceClient(connectionString);
			if (!conn.IsReady)
				throw new Exception($"Unable to connect to CRM: {conn.LastCrmError}");
			conn.OrganizationServiceProxy.Timeout = new TimeSpan(0, 5, 0);
			return new ExtendedOrganizationService(conn, environmentName, readOnly);
		}

		/// <summary>
		/// Creates a new ExtendedOrganizationService from a connection string stored in app.config / web.config file
		/// </summary>
		/// <param name="connectionStringIndex">The index of the connection string in the configuration file</param>
		/// <param name="environmentName">The friendly name of the environment that is being connected to e.g. DEV, TEST, PROD</param>
		/// <param name="readOnly">If true, any calls to Dynamics which modify data will only be simulated.</param>
		public static ExtendedOrganizationService FromConfiguration(string connectionStringIndex, string environmentName, bool readOnly)
		{
			string connectionString;
			try
			{
				connectionString = ConfigurationManager.ConnectionStrings[connectionStringIndex].ToString();
			}
			catch (Exception e)
			{
				throw new Exception("Connection string could not be loaded", e);
			}

			return FromConnectionString(connectionString, environmentName, readOnly);
		}

		private readonly Type[] _allowedMessagesWhenActionChangesDisabled =
		{
			typeof(RetrieveDependenciesForDeleteRequest),
			typeof(RetrieveAttributeRequest),
			typeof(RetrieveRequest),
			typeof(RetrieveMultipleRequest)
		};

		#region Write Methods

		public Guid Create(Entity entity)
		{
			var response = Execute(new CreateRequest()
			{
				Target = entity
			});

			if (response == null)
				return new Guid();

			return ((CreateResponse)response).id;
		}

		public void Update(Entity entity)
		{
			Execute(new UpdateRequest()
			{
				Target = entity
			});
		}

		public void Delete(string entityName, Guid id)
		{
			Execute(new DeleteRequest()
			{
				Target = new EntityReference(entityName, id)
			});
		}

		public void Associate(string entityName, Guid entityId, Relationship relationship,
			EntityReferenceCollection relatedEntities)
		{
			Execute(new AssociateRequest()
			{
				Target = new EntityReference(entityName, entityId),
				Relationship = relationship,
				RelatedEntities = relatedEntities
			}
			);
		}

		public void Disassociate(string entityName, Guid entityId, Relationship relationship,
			EntityReferenceCollection relatedEntities)
		{
			Execute(new DisassociateRequest()
			{
				Target = new EntityReference(entityName, entityId),
				Relationship = relationship,
				RelatedEntities = relatedEntities
			}
			);
		}

		#endregion

		#region ReadOnly Methods

		public EntityCollection RetrieveMultiple(QueryBase query)
		{
			return _crmService.RetrieveMultiple(query);
		}

		public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
		{
			return _crmService.Retrieve(entityName, id, columnSet);
		}

		#endregion

		public OrganizationResponse Execute(OrganizationRequest request)
		{
			Log.Debug(
				$"Executing {request.RequestName} message ({(!_readOnly ? "Live execution on Dynamics" : "Simulation only")})...");

			if (_readOnly &&
				!_allowedMessagesWhenActionChangesDisabled.Contains(request.GetType()))
			{
				Log.Debug($"Message type {request} has been simulated because connection is Read Only...");
				return null;
			}

			Log.Debug(
				$"Executing {request.RequestName} message on Dynamics...");
			return  _crmService.Execute(request);
		}

		/// <summary>
		/// Retrieves all pages of records resulting from the provided fetch query
		/// </summary>
		/// <param name="fetchXML">A FetchExpression containing the fetch query. Ensure that the first line of the FetchXML query contains a parameter for the paging cookie, i.e. &lt;fetch &#123;0&#125 &gt;</param>
		/// <returns>List of all entity records resulting from the query</returns>
		public List<Entity> RetrieveAllRecords(string fetchXML)
		{
			bool moreRecords;
			var page = 1;
			var cookie = string.Empty;
			var entities = new List<Entity>();
			do
			{
				var xml = string.Format(fetchXML, cookie);
				var retrieveRequest = new RetrieveMultipleRequest() { Query = new FetchExpression(fetchXML) };
				var collection = ((RetrieveMultipleResponse)_crmService.Execute(retrieveRequest)).EntityCollection;

				if (collection.Entities.Count >= 0) entities.AddRange(collection.Entities);

				moreRecords = collection.MoreRecords;
				if (!moreRecords) continue;
				page++;
				cookie = $"paging-cookie='{System.Security.SecurityElement.Escape(collection.PagingCookie)}' page='{page}'";
			} while (moreRecords);

			return entities;
		}

		public IEnumerable<OneToManyRelationshipMetadata> GetOneToManyRelationships(string entityName)
		{
			var retrieveEntityRequest = new RetrieveEntityRequest
			{
				EntityFilters = EntityFilters.Relationships,
				LogicalName = entityName
			};

			var entityRelationships = (RetrieveEntityResponse)_crmService.Execute(retrieveEntityRequest);
			return entityRelationships.EntityMetadata.OneToManyRelationships;
		}

		public IExecuteMultipleResponseAdapter ExecuteMultipleReturnAdapter(ExecuteMultipleRequest executeMultipleRequest)
		{
			return new ExecuteMultipleResponseAdapter((ExecuteMultipleResponse) Execute(executeMultipleRequest));
		}
	}
}
