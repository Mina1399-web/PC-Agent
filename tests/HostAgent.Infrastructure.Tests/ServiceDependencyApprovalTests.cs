using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ServiceDependencyApprovalTests
{
    private static HelperOperationRequest Request(string[]? dependencies) => new(1, Guid.NewGuid(),
        "incident-1", "diagnosis-1", 90, ["evidence-1"], HelperOperation.StartService,
        new ServiceParameters("service-a", dependencies), null);

    private static HelperAllowlist Allowlist() => new(
    [
        new("service-a", "ServiceA", [HelperOperation.StartService]),
        new("dep-b", "DependencyB", [HelperOperation.StartService]),
        new("dep-c", "DependencyC", [HelperOperation.StartService])
    ], []);

    [Fact]
    public void Binding_includes_each_approved_dependency_in_request_order()
    {
        var request = Request(["dep-b", "dep-c"]);
        var binding = Allowlist().Resolve(request);
        Assert.NotNull(binding);
        Assert.Equal(new[] { "DependencyB", "DependencyC" }, binding.ApprovedDependencyNames);
    }

    [Fact]
    public void Unknown_or_non_start_allowlisted_dependencies_are_denied()
    {
        Assert.Null(Allowlist().Resolve(Request(["not-registered"])));
        var stopOnly = new HelperAllowlist(
        [
            new("service-a", "ServiceA", [HelperOperation.StartService]),
            new("dep-b", "DependencyB", [HelperOperation.StopService])
        ], []);
        Assert.Null(stopOnly.Resolve(Request(["dep-b"])));
    }

    [Fact]
    public void Service_group_dependencies_are_rejected_instead_of_partially_enumerated()
    {
        Assert.Throws<InvalidDataException>(() => WindowsServiceDependencyReader.ParseDependencyNames(["+ExampleGroup"]));
        Assert.Throws<InvalidDataException>(() => WindowsServiceDependencyReader.ParseDependencyNames(["Bad/Name"]));
        Assert.Equal(new[] { "DependencyB" }, WindowsServiceDependencyReader.ParseDependencyNames(["DependencyB"]));
    }

    [Fact]
    public void Dependency_scope_round_trips_and_changes_the_approval_digest()
    {
        var scoped = Request(["dep-b"]);
        var parsed = HelperProtocol.ParseRequest(HelperProtocol.SerializeRequest(scoped));
        Assert.Equal(new[] { "dep-b" }, Assert.IsType<ServiceParameters>(parsed.Parameters).ExpectedDependencyIds);
        Assert.NotEqual(HelperProtocol.Digest(scoped), HelperProtocol.Digest(scoped with
        {
            Parameters = new ServiceParameters("service-a", ["dep-c"])
        }));
    }

    [Fact]
    public async Task Executor_forwards_approval_bound_dependency_names_to_windows_api()
    {
        var request = Request(["dep-b"]);
        var binding = Assert.IsType<OperationBinding>(Allowlist().Resolve(request));
        var api = new FakeWindowsOperationApi();
        await new WindowsOperationExecutor(api).ExecuteAsync(request, binding, CancellationToken.None);
        Assert.Equal(new[] { "DependencyB" }, api.ExpectedDependencies);
    }

    private sealed class FakeWindowsOperationApi : IWindowsOperationApi
    {
        public IReadOnlyCollection<string>? ExpectedDependencies { get; private set; }
        public ValueTask SetServiceRunningAsync(string serviceName, bool running, CancellationToken cancellationToken,
            IReadOnlyCollection<string>? expectedDependencies = null)
        {
            Assert.Equal("ServiceA", serviceName);
            Assert.True(running);
            ExpectedDependencies = expectedDependencies;
            return ValueTask.CompletedTask;
        }
        public IProcessLease OpenProcess(int pid) => throw new NotSupportedException();
    }
}
