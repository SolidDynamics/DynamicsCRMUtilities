using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace FluidDynamics.DynamicsCRMUtilities
{
	public interface IExecuteMultipleResponseAdapter
	{
		bool IsFaulted { get; }
		ExecuteMultipleResponseItemCollection Responses { get; }
	}

	public class ExecuteMultipleResponseAdapter : IExecuteMultipleResponseAdapter
	{
		private readonly ExecuteMultipleResponse _executeMultipleResponse;

		public bool IsFaulted => _executeMultipleResponse.IsFaulted;

		public ExecuteMultipleResponseItemCollection Responses => _executeMultipleResponse.Responses;

		public ExecuteMultipleResponseAdapter(ExecuteMultipleResponse executeMultipleResponse)
		{
			_executeMultipleResponse = executeMultipleResponse;
		}
	}

}
