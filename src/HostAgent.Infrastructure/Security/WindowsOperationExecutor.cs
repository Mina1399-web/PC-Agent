using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Security;

public sealed record ProcessIdentity(int Pid, long StartTimeUtcTicks, string ExecutablePath, string Sha256, bool IsCritical);
public interface IProcessLease : IDisposable
{
    ProcessIdentity Identity { get; }
    void Terminate();
}
public interface IWindowsOperationApi
{
    ValueTask SetServiceRunningAsync(string serviceName, bool running, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? expectedDependencies = null);
    IProcessLease OpenProcess(int pid);
}
public sealed class WindowsOperationExecutor(IWindowsOperationApi api) : IOperationExecutor
{
    public async ValueTask ExecuteAsync(HelperOperationRequest request, OperationBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = HelperProtocol.ParseRequest(HelperProtocol.SerializeRequest(request));
        if (binding.TargetKey != HelperProtocol.TargetKey(request)) throw new UnauthorizedAccessException("target_mismatch");
        if (request.Operation == HelperOperation.TerminateProcess && request.Parameters is ProcessParameters parameters)
        {
            using var lease = api.OpenProcess(parameters.Pid);
            var actual = lease.Identity;
            if (actual.Pid != parameters.Pid || actual.StartTimeUtcTicks != parameters.StartTimeUtcTicks ||
                actual.IsCritical || binding.ExecutablePath is null || binding.Sha256 is null ||
                !string.Equals(actual.ExecutablePath, binding.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actual.Sha256, binding.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("process_identity_mismatch");
            cancellationToken.ThrowIfCancellationRequested();
            lease.Terminate();
            return;
        }
        if (binding.ServiceName is not { } service || request.Parameters is not ServiceParameters)
            throw new UnauthorizedAccessException("invalid_service_binding");
        switch (request.Operation)
        {
            case HelperOperation.StartService:
                var startParameters = (ServiceParameters)request.Parameters;
                if (startParameters.ExpectedDependencyIds is not null && binding.ApprovedDependencyNames is null)
                    throw new UnauthorizedAccessException("service_dependency_policy_mismatch");
                await api.SetServiceRunningAsync(service, true, cancellationToken,
                    startParameters.ExpectedDependencyIds is null ? null : binding.ApprovedDependencyNames);
                break;
            case HelperOperation.StopService: await api.SetServiceRunningAsync(service, false, cancellationToken); break;
            case HelperOperation.RestartService:
                var restartParameters = (ServiceParameters)request.Parameters;
                if (restartParameters.ExpectedDependencyIds is not null && binding.ApprovedDependencyNames is null)
                    throw new UnauthorizedAccessException("service_dependency_policy_mismatch");
                await api.SetServiceRunningAsync(service, false, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await api.SetServiceRunningAsync(service, true, cancellationToken,
                    restartParameters.ExpectedDependencyIds is null ? null : binding.ApprovedDependencyNames);
                break;
            default: throw new UnauthorizedAccessException("unsupported_operation");
        }
    }
}
