using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
using System.Text.RegularExpressions;
namespace HostAgent.Infrastructure.Security;
public sealed record ServiceRegistration(string Id, string ServiceName, HelperOperation[] EnabledOperations);
public sealed record ProcessRegistration(string Id, string ExecutablePath, string Sha256, bool Enabled);
public sealed record OperationBinding(string TargetKey, string? ServiceName, string? ExecutablePath, string? Sha256);
public sealed class HelperAllowlist
{
    private readonly Dictionary<string, ServiceRegistration> _services;
    private readonly Dictionary<string, ProcessRegistration> _processes;
    public HelperAllowlist(IEnumerable<ServiceRegistration> services, IEnumerable<ProcessRegistration> processes)
    {
        _services = services.ToDictionary(s => s.Id, s => s with { EnabledOperations = s.EnabledOperations.ToArray() }, StringComparer.Ordinal);
        _processes = processes.ToDictionary(p => p.Id, StringComparer.Ordinal);
        if (_services.Values.Select(s => s.ServiceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _services.Count ||
            _processes.Values.Select(p => Path.GetFullPath(p.ExecutablePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _processes.Count)
            throw new ArgumentException("duplicate_local_target");
        foreach (var service in _services.Values)
            if (!SafeId(service.Id) || !SafeId(service.ServiceName) || service.EnabledOperations.Any(o =>
                !Enum.IsDefined(o) || o == HelperOperation.TerminateProcess)) throw new ArgumentException("invalid_service_registration");
        foreach (var process in _processes.Values)
            if (!SafeId(process.Id) || !Path.IsPathFullyQualified(process.ExecutablePath) ||
                process.ExecutablePath.StartsWith(@"\\", StringComparison.Ordinal) || process.ExecutablePath.AsSpan(2).Contains(':') ||
                !Regex.IsMatch(process.Sha256, "\\A[0-9A-Fa-f]{64}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new ArgumentException("invalid_process_registration");
    }
    public OperationBinding? Resolve(HelperOperationRequest request)
    {
        if (request.Operation == HelperOperation.TerminateProcess && request.Parameters is ProcessParameters process &&
            process.Pid > 4 && process.StartTimeUtcTicks > 0 && _processes.TryGetValue(process.ProcessId, out var registration) && registration.Enabled)
            return new(HelperProtocol.TargetKey(request), null, Path.GetFullPath(registration.ExecutablePath), registration.Sha256);
        if (request.Operation != HelperOperation.TerminateProcess && Enum.IsDefined(request.Operation) &&
            request.Parameters is ServiceParameters service && _services.TryGetValue(service.ServiceId, out var local) && local.EnabledOperations.Contains(request.Operation))
            return new(HelperProtocol.TargetKey(request), local.ServiceName, null, null);
        return null;
    }
    private static bool SafeId(string value) => Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\\z",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) && !value.Contains("..", StringComparison.Ordinal);
}
