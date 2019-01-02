using Microsoft.Xrm.Sdk;
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

				foreach (var recordId in batch)
				{
					_crmService.Delete(entityName, recordId);
				}
			}
		}


		private IEnumerable<Guid> GetDependentRecords(RestrictDeleteDependency restrictDeleteDependency, IEnumerable<Guid> requiredRecordIds)
		{
			var queryFilter = new FilterExpression();
			queryFilter.Conditions.Add(
				new ConditionExpression(
					restrictDeleteDependency.DependentEntityLookupField,
					ConditionOperator.In,
					requiredRecordIds.Select(i => new EntityReference(restrictDeleteDependency.RequiredEntity, i)
					)
				)
			);

			var query = new QueryExpression(restrictDeleteDependency.DependentEntity)
			{
				ColumnSet = new ColumnSet(new[] { restrictDeleteDependency.DependentEntity + "id" }),
				Criteria = queryFilter
			};

			return _crmService.RetrieveMultiple(query).Entities.Select(e => e.Id);
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
