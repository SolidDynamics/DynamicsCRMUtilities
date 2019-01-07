using Humanizer;
using log4net;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using FluidDynamics.DynamicsCRMUtilities;

namespace FluidDynamics.CascadeDelete
{
	public class CascadeDeleter
	{
		private const string SUCCESSFUL_STRING = "Success";
		private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		private readonly IExtendedOrganizationService _crmService;

		public int BatchSize { get; set; } = 1000;

		public CascadeDeleter(IExtendedOrganizationService organizationService)
		{
			_crmService = organizationService;
		}

		public IList<DeleteResult> CascadeDeleteRecords(string entityName, IEnumerable<Guid> ids)
		{
			log.Info($"Starting Cascade Delete for {entityName}...");
			var deleteResults = new List<DeleteResult>();

			var restrictDeleteDependencies = GetRestrictDeleteRelationships(entityName);

			log.Info($"Entity {entityName} has " +
				$"{"restrict delete dependency".ToQuantity(restrictDeleteDependencies.Count())}" +
				$": ({string.Join(",", restrictDeleteDependencies.Select(r => r.DependentEntity))})");

			var batches = ids.Batches(BatchSize);
			int numberOfBatches = batches.Count();
			log.Info($"{"record".ToQuantity(ids.Count())} divided into {"batch".ToQuantity(numberOfBatches)}");

			int batchCount = 0;
			foreach (var batch in batches)
			{
				batchCount++;
				log.Info($"Processing batch {batchCount} of {numberOfBatches}");

				if (restrictDeleteDependencies.Any())
				{
					foreach (var restrictDeleteDependency in restrictDeleteDependencies)
					{
						var dependentRecords = GetDependentRecords(restrictDeleteDependency, batch);
						log.Info($"Found {"dependent records".ToQuantity(dependentRecords.Count())} on entity {restrictDeleteDependency.DependentEntity} in this batch");
						deleteResults.AddRange(CascadeDeleteRecords(restrictDeleteDependency.DependentEntity, dependentRecords));
					}
				}

				var executeMultipleRequest = new ExecuteMultipleRequest()
				{
					Requests = new OrganizationRequestCollection()
				};
				foreach (var id in batch)
				{
					executeMultipleRequest.Requests.Add(new DeleteRequest { Target = new EntityReference(entityName, id) });
				}

				log.Info($"Executing requests for batch on entity {entityName}");
				var deleteMultipleResponse = _crmService.ExecuteMultipleReturnAdapter(executeMultipleRequest);
				var responses = GetDeleteResults(entityName, deleteMultipleResponse);

				var responsesList = responses.ToList();
				log.Info($"Batch completed with {"successes".ToQuantity(responsesList.Count(x => x.Result == SUCCESSFUL_STRING))} of {batch.Count()}");
				deleteResults.AddRange(responsesList);
			}

			log.Info($"All batches completed for {entityName}...");
			return deleteResults;
		}

		internal virtual IEnumerable<DeleteResult> GetDeleteResults(string entityName, IExecuteMultipleResponseAdapter deleteMultipleResponse)
		{
			return deleteMultipleResponse.Responses.Select(d => new DeleteResult()
			{
				EntityName = entityName,
				RecordID = d.Response.Results["id"].ToString(),
				Result = d.Fault == null ? SUCCESSFUL_STRING : d.Fault.Message
			});
		}

		private IEnumerable<Guid> GetDependentRecords(RestrictDeleteDependency restrictDeleteDependency, IEnumerable<Guid> requiredRecordIds)
		{
			var query = "<fetch {0}" +
				$@"<entity name='{restrictDeleteDependency.DependentEntity}'>
					<attribute name='{restrictDeleteDependency.DependentEntity}id' />
					<filter>
						<condition attribute='{restrictDeleteDependency.DependentEntityLookupField}' operator='in'>
							{string.Join(string.Empty, requiredRecordIds.Select(id => $"<value>{id}</value>")) }
						</condition>
					</filter>
				</entity>
				</fetch>";

			log.Debug($"Getting dependent records with query:\n{query}");
			return _crmService.RetrieveAllRecords(query).Select(e => e.Id);
		}

		private IEnumerable<RestrictDeleteDependency> GetRestrictDeleteRelationships(string entityName)
		{
			var oneToManyRelationships = _crmService.GetOneToManyRelationships(entityName);
			foreach (OneToManyRelationshipMetadata relationship in oneToManyRelationships)
			{
				if (relationship.CascadeConfiguration.Delete == CascadeType.Restrict)
				{
					yield return new RestrictDeleteDependency() { DependentEntity = relationship.ReferencingEntity, DependentEntityLookupField = relationship.ReferencingEntityNavigationPropertyName, RequiredEntity = entityName };
				}
			}
		}
	}

	internal class RestrictDeleteDependency
	{
		public string RequiredEntity { get; set; }

		public string DependentEntity { get; set; }

		public string DependentEntityLookupField { get; set; }
	}

	public class DeleteResult
	{
		public string EntityName { get; set; }
		public string RecordID { get; set; }
		public string Result { get; set; }
	}

	public static class HelperMethods
	{
		public static IEnumerable<IEnumerable<T>> Batches<T>(this IEnumerable<T> source, int size)
		{
			var count = 0;
			using (var iter = source.GetEnumerator())
			{
				while (iter.MoveNext())
				{
					var chunk = new T[size];
					count = 1;
					chunk[0] = iter.Current;
					for (var i = 1; i < size && iter.MoveNext(); i++)
					{
						chunk[i] = iter.Current;
						count++;
					}
					if (count < size)
					{
						Array.Resize(ref chunk, count);
					}
					yield return chunk;
				}
			}
		}
	}
}
