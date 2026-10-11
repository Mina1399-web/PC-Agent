using HostAgent.Core.Models.Diagnosis;

namespace HostAgent.Core.Contracts;

public interface IDiagnosticEngine
{
    ValueTask<IReadOnlyList<Diagnosis>> EvaluateAsync(
        HostEvidence evidence,
        CancellationToken cancellationToken = default);
}
