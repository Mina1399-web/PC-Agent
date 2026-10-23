using Xunit;
using Xunit.Sdk;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteProbeGuardVmIntegrationTests
{
    [Fact]
    public void Vm_guard_reads_the_approved_machine_identity_without_com_enumerator_failure()
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(Environment.MachineName, "AGENTPC", StringComparison.OrdinalIgnoreCase))
            throw SkipException.ForSkip("The live identity integration check runs only inside the approved HostAgent-DEV VM.");

        // This regression verifies the read-only UUID/COM identity reader,
        // not permission to run an old chaos probe's resource-intensive work.
        CompleteProbeGuard.ValidateIdentity();
    }
}
