using FluidDynamics.CascadeDelete;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluidDynamics.DynamicsCRMUtilities.Tests
{
	[TestClass]
	public class CascadeDeleterTests
	{
		private const string entityName = "testEntity";
		private const string relatedEntityName = "relatedEntity";

		private readonly List<Guid> RecordsToDelete = new List<Guid>()
		{
			new Guid("5afef53e-96d8-4527-bbbc-ee86fff4183a"),
			new Guid("30aba5c1-3852-461e-b0d8-15bd95ac72ca"),
			new Guid("0a33b533-0b8c-4a7b-a57a-80c3b1fcb9ce")
		};

		private readonly List<Guid> DependentRecordIds = new List<Guid>()
		{
			new Guid("b813b59f-7a61-443e-b01c-53d8de33b302"),
			new Guid("34a91707-85a7-4e40-b41a-e7ad133b60fb"),
			new Guid("3bea82e1-f981-478b-acbe-7a48d76b1373"),
			new Guid("a29b0510-0dcc-4a28-8f2c-2b296d8cf8bd")
		};

		[TestMethod]
		public void CascadeDeleteWithNoDependencies_DeletesRecords()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();

			var executeMultipleResponse = GetExecuteMultipleResponseAdapter(RecordsToDelete);

			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3)))
				.Returns(executeMultipleResponse.Object)
				.Verifiable();

			var cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			var results = cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();

			Assert.IsTrue(RecordsToDelete.SequenceEqual(results.Select(r => new Guid(r.RecordID))));
		}

		[TestMethod]
		public void CascadeDeleteWithNoDependencies_DeletesRecordsInBatches()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);

			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();

			var firstBatchResponse = GetExecuteMultipleResponseAdapter(RecordsToDelete.Take(2));
			var secondBatchResponse = GetExecuteMultipleResponseAdapter(RecordsToDelete.Skip(2));

			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 2))).Returns(firstBatchResponse.Object).Verifiable();
			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 1))).Returns(secondBatchResponse.Object).Verifiable();

			var cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object)
			{
				BatchSize = 2
			};

			var results = cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);
			mockOrganizationService.Verify();
			Assert.IsTrue(RecordsToDelete.SequenceEqual(results.Select(r => new Guid(r.RecordID))));
		}

	
		[TestMethod]
		public void WithRestrictDeleteDependenciesButNoDependentRecords_RecordsDeleted()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);

			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(GetOneToManyRelationshipMetadata()).Verifiable();
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(relatedEntityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();

			mockOrganizationService.Setup(o => o.RetrieveAllRecords(It.IsAny<string>())).Returns(new List<Entity>()).Verifiable();

			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3)))
				.Returns(GetExecuteMultipleResponseAdapter(RecordsToDelete).Object).Verifiable();

			var cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			var results = cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);
			mockOrganizationService.Verify();
			Assert.IsTrue(RecordsToDelete.SequenceEqual(results.Select(r => new Guid(r.RecordID))));
		}

		private static IEnumerable<OneToManyRelationshipMetadata> GetOneToManyRelationshipMetadata()
		{
			return new OneToManyRelationshipMetadata[]
			{ new OneToManyRelationshipMetadata()
				{
					ReferencingEntity = relatedEntityName,
					ReferencingEntityNavigationPropertyName = "relatedAttribute",
					CascadeConfiguration = new CascadeConfiguration()
					{
						Delete = CascadeType.Restrict
					}
				}
			};
		}

		[TestMethod]
		public void WithRestrictDeleteDependenciesAndDependentRecords_RecordsDeleted()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);

			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(GetOneToManyRelationshipMetadata()).Verifiable();
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(relatedEntityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();

			mockOrganizationService.Setup(
					o => o.RetrieveAllRecords(It.IsAny<string>()))
				.Returns(DependentRecordIds.Select(d => new Entity {Id = d}).ToList())
				.Verifiable();
		
			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 4)))
				.Returns(GetExecuteMultipleResponseAdapter(DependentRecordIds).Object).Verifiable();
			mockOrganizationService.Setup(o => o.ExecuteMultipleReturnAdapter(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3)))
				.Returns(GetExecuteMultipleResponseAdapter(RecordsToDelete).Object).Verifiable();

			var cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			var results = cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();
			Assert.IsTrue(DependentRecordIds.Concat(RecordsToDelete).SequenceEqual(results.Select(r => new Guid(r.RecordID))));
		}

		private Mock<IExecuteMultipleResponseAdapter> GetExecuteMultipleResponseAdapter(IEnumerable<Guid> recordIds)
		{
			var executeMultipleResponseAdapter = new Mock<IExecuteMultipleResponseAdapter>();
			var responseCollection = new ExecuteMultipleResponseItemCollection();
			responseCollection.AddRange(recordIds.Select(r => new ExecuteMultipleResponseItem()
			{
				Fault = null,
				Response = new DeleteResponse()
				{
					ResponseName = "delete",
					Results = new ParameterCollection { { "id", r } }
				}
			}));
			executeMultipleResponseAdapter
				.Setup(e => e.Responses)
				.Returns(responseCollection);
			return executeMultipleResponseAdapter;
		}

	}
}