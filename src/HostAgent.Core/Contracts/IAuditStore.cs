using HostAgent.Core.Models.Audit;
namespace HostAgent.Core.Contracts;
public interface IAuditStore
{
    ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken);
}
