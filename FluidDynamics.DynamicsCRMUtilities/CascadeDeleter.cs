using log4net;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluidDynamics.CascadeDelete
{
	public class CascadeDeleter
	{
		private readonly IExtendedOrganizationService _crmService;

		public int BatchSize { get; set; } = 1000;

		public CascadeDeleter(IExtendedOrganizationService organizationService)
		{
			_crmService = organizationService;
		}

		public void CascadeDeleteRecords(string entityName, IEnumerable<Guid> ids)
		{
			var restrictDeleteDependencies = GetRestrictDeleteRelationships(entityName);

			var batches = ids.Batches(BatchSize);
			foreach (var batch in batches)
			{
				if (restrictDeleteDependencies.Any())
				{
					foreach (var restrictDeleteDependency in restrictDeleteDependencies)
					{
						var dependentRecords = GetDependentRecords(restrictDeleteDependency, batch);
						CascadeDeleteRecords(restrictDeleteDependency.DependentEntity, dependentRecords);
					}
				}

				var executeMultipleRequest = new ExecuteMultipleRequest()
				{
					Requests = new OrganizationRequestCollection()
				};
				foreach(var id in batch)
				{
					executeMultipleRequest.Requests.Add(new DeleteRequest() { Target = new EntityReference(entityName, id) });
				}

				_crmService.Execute(executeMultipleRequest);
			}	
		}


		private IEnumerable<Guid> GetDependentRecords(RestrictDeleteDependency restrictDeleteDependency, IEnumerable<Guid> requiredRecordIds)
		{
			var query = "<fetch {0}" +
				$@"<entity name='{restrictDeleteDependency.DependentEntity}'>
					<attribute name='{restrictDeleteDependency.DependentEntity}id' />
					<filter>
						<condition attribute='{restrictDeleteDependency.DependentEntityLookupField}' operator='in'>
							{string.Join(string.Empty, requiredRecordIds.Select(id => $"<value>{id}</value")) }
						</condition>
					</filter>
				</entity>
				</fetch>";
			
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
