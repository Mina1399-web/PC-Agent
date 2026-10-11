using HostAgent.Core.Models.Remediation;
namespace HostAgent.Core.Contracts;
public interface IHelperClient
{
    ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken cancellationToken);
}
