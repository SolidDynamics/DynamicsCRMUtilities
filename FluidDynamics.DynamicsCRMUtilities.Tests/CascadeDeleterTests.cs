using FluidDynamics.CascadeDelete;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;
using System;
using System.Collections.Generic;

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

		[TestMethod]
		public void CascadeDeleteWithNoDependencies_DeletesRecords()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();
			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3))).Returns(new ExecuteMultipleResponse()).Verifiable();

			CascadeDeleter cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();
		}

		[TestMethod]
		public void CascadeDeleteWithNoDependencies_DeletesRecordsInBatches()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);
		
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();
			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 2))).Returns(new ExecuteMultipleResponse()).Verifiable();
			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 1))).Returns(new ExecuteMultipleResponse()).Verifiable();

			CascadeDeleter cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object)
			{
				BatchSize = 2
			};

			cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();
		}

		[TestMethod]
		public void WithRestrictDeleteDependenciesButNoDependentRecords_RecordsDeleted()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);

			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[]

			{ new OneToManyRelationshipMetadata()
				{
					ReferencingEntity = relatedEntityName,
					ReferencingEntityNavigationPropertyName = "relatedAttribute",
					CascadeConfiguration = new CascadeConfiguration()
						{
							Delete = CascadeType.Restrict
						}
			}
			}).Verifiable();
			mockOrganizationService.Setup(o => o.RetrieveAllRecords(It.IsAny<string>())).Returns(new List<Entity>()).Verifiable();
			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3))).Returns(new ExecuteMultipleResponse()).Verifiable();

			CascadeDeleter cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();
		}

		[TestMethod]
		public void WithRestrictDeleteDependenciesAndDependentRecords_RecordsDeleted()
		{
			var mockOrganizationService = new Mock<IExtendedOrganizationService>(MockBehavior.Strict);

			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(entityName)).Returns(new OneToManyRelationshipMetadata[]
			{ new OneToManyRelationshipMetadata()
				{
					ReferencingEntity = relatedEntityName,
					ReferencingEntityNavigationPropertyName = "relatedAttribute",
					CascadeConfiguration = new CascadeConfiguration()
						{
							Delete = CascadeType.Restrict
						}
			}
			}).Verifiable();
			mockOrganizationService.Setup(o => o.GetOneToManyRelationships(relatedEntityName)).Returns(new OneToManyRelationshipMetadata[] { }).Verifiable();

			mockOrganizationService.Setup(o => o.RetrieveAllRecords(It.IsAny<string>())).Returns(new List<Entity>()
			{
				new Entity() { Id = Guid.NewGuid() },
				new Entity() { Id = Guid.NewGuid() },
				new Entity() { Id = Guid.NewGuid() },
				new Entity() { Id = Guid.NewGuid() }
			}).Verifiable();

			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 4))).Returns(new ExecuteMultipleResponse()).Verifiable();
			mockOrganizationService.Setup(o => o.Execute(It.Is<ExecuteMultipleRequest>(r => r.Requests.Count == 3))).Returns(new ExecuteMultipleResponse()).Verifiable();

			CascadeDeleter cascadeDeleter = new CascadeDeleter(mockOrganizationService.Object);
			cascadeDeleter.CascadeDeleteRecords(entityName, RecordsToDelete);

			mockOrganizationService.Verify();
		}
	}
}