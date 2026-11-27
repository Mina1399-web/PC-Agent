using System.Buffers.Binary;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsVolumeDiskTopologyTests
{
    [Fact]
    public async Task Maps_one_volume_to_one_os_visible_disk_with_provenance()
    {
        var path = VolumePath();
        var native = new FakeNative([path], (_, capacity) =>
            new(VolumeExtentQueryStatus.Success, 1, [0]));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal("available", report.State.Status);
        Assert.Equal(WindowsVolumeDiskTopologyReader.Source, report.State.Source);
        Assert.NotNull(report.State.AtUtc);
        Assert.Single(report.Volumes);
        var volume = report.Volumes[0];
        Assert.Equal(path, volume.VolumeGuidPath);
        Assert.Equal(new uint[] { 0 }, volume.DiskNumbers);
        Assert.Equal("available", volume.State.Status);
        Assert.Equal(report.State.AtUtc, volume.State.AtUtc);
        Assert.Equal("os_visible_observation", volume.DiskNumberScope);
        Assert.Equal("unknown", volume.UnderlyingMediaStatus);
        Assert.Equal("underlying_media_not_resolved", volume.UnderlyingMediaReason);
    }

    [Fact]
    public async Task Retries_more_data_once_and_deduplicates_disk_numbers()
    {
        var path = VolumePath();
        var native = new FakeNative([path], (_, capacity) => capacity == 1
            ? new(VolumeExtentQueryStatus.MoreData, 3, [])
            : new(VolumeExtentQueryStatus.Success, 3, [7, 2, 7]));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal("available", report.State.Status);
        Assert.Equal(3, report.ExtentsRead);
        Assert.Equal(new uint[] { 2, 7 }, report.Volumes.Single().DiskNumbers);
        Assert.Equal(new[] { 1, 3 }, native.QueryCapacities);
    }

    [Fact]
    public async Task Volume_spanning_multiple_disks_preserves_all_distinct_disk_numbers()
    {
        var path = VolumePath();
        var native = new FakeNative([path], (_, _) =>
            new(VolumeExtentQueryStatus.Success, 3, [4, 9, 4]));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal("available", report.State.Status);
        Assert.Equal(new uint[] { 4, 9 }, report.Volumes.Single().DiskNumbers);
        Assert.Equal(3, report.ExtentsRead);
    }

    [Theory]
    [InlineData((int)VolumeExtentQueryStatus.AccessDenied, "unavailable", "access_denied")]
    [InlineData((int)VolumeExtentQueryStatus.Unsupported, "unavailable", "unsupported_source")]
    [InlineData((int)VolumeExtentQueryStatus.Disappeared, "unavailable", "volume_disappeared")]
    [InlineData((int)VolumeExtentQueryStatus.InvalidBuffer, "unknown", "invalid_extent_buffer")]
    public async Task Failed_volume_query_never_claims_a_mapping_or_health(
        int queryStatusValue, string expectedStatus, string expectedReason)
    {
        var queryStatus = (VolumeExtentQueryStatus)queryStatusValue;
        var native = new FakeNative([VolumePath()], (_, _) =>
            new(queryStatus, 0, [], queryStatus == VolumeExtentQueryStatus.InvalidBuffer ? "invalid_extent_buffer" : null));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal("partial", report.State.Status);
        Assert.Equal(expectedStatus, report.Volumes.Single().State.Status);
        Assert.Equal(expectedReason, report.Volumes.Single().State.Reason);
        Assert.Empty(report.Volumes.Single().DiskNumbers);
        Assert.NotEqual("healthy", report.State.Status);
    }

    [Fact]
    public async Task Volume_without_drive_letter_is_still_reported_by_guid_path()
    {
        var path = VolumePath();
        var native = new FakeNative([path], (_, _) =>
            new(VolumeExtentQueryStatus.Success, 1, [12]));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal(path, report.Volumes.Single().VolumeGuidPath);
        Assert.Equal(new uint[] { 12 }, report.Volumes.Single().DiskNumbers);
        Assert.DoesNotContain(':', report.Volumes.Single().VolumeGuidPath);
    }

    [Fact]
    public void Rejects_a_truncated_or_malformed_extent_buffer()
    {
        var buffer = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), 3);

        Assert.False(WindowsVolumeDiskTopologyReader.TryParseExtents(buffer, 31, 1,
            out _, out _, out _));
        Assert.False(WindowsVolumeDiskTopologyReader.TryParseExtents(new byte[3], 3, 1,
            out _, out _, out _));
    }

    [Fact]
    public async Task Volume_limit_is_bounded_and_reported_as_partial()
    {
        var paths = Enumerable.Range(0, WindowsVolumeDiskTopologyReader.MaximumVolumes + 5)
            .Select(_ => VolumePath()).ToArray();
        var native = new FakeNative(paths, (_, _) =>
            new(VolumeExtentQueryStatus.Success, 1, [0]));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal(WindowsVolumeDiskTopologyReader.MaximumVolumes, report.VolumesEnumerated);
        Assert.Equal(WindowsVolumeDiskTopologyReader.MaximumVolumes, native.QueryCount);
        Assert.Equal("partial", report.State.Status);
        Assert.Equal("volume_limit", report.State.Reason);
    }

    [Fact]
    public async Task Completed_empty_enumeration_is_available_and_distinct_from_failure()
    {
        var empty = new FakeNative([], (_, _) => throw new InvalidOperationException("No query expected."),
            () => new([], true));
        var failed = new FakeNative([], (_, _) => throw new InvalidOperationException("No query expected."),
            () => new([], false, "access_denied"));

        var emptyReport = await new WindowsVolumeDiskTopologyReader(empty, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();
        var failedReport = await new WindowsVolumeDiskTopologyReader(failed, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal("available", emptyReport.State.Status);
        Assert.Null(emptyReport.State.Reason);
        Assert.Empty(emptyReport.Volumes);
        Assert.Equal("unavailable", failedReport.State.Status);
        Assert.Equal("access_denied", failedReport.State.Reason);
    }

    [Fact]
    public async Task Total_extent_limit_does_not_return_a_partial_native_map_as_complete()
    {
        var paths = new[] { VolumePath(), VolumePath() };
        var native = new FakeNative(paths, (path, capacity) => path == paths[0]
            ? capacity == 1
                ? new(VolumeExtentQueryStatus.MoreData, WindowsVolumeDiskTopologyReader.MaximumExtents, [])
                : new(VolumeExtentQueryStatus.Success, WindowsVolumeDiskTopologyReader.MaximumExtents,
                    Enumerable.Range(0, WindowsVolumeDiskTopologyReader.MaximumExtents).Select(i => (uint)i).ToArray())
            : throw new InvalidOperationException("Second volume should be skipped at the global extent bound."));

        var report = await new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromSeconds(1)).ReadAsync();

        Assert.Equal(WindowsVolumeDiskTopologyReader.MaximumExtents, report.ExtentsRead);
        Assert.Equal("partial", report.State.Status);
        Assert.Equal("extent_limit", report.State.Reason);
        Assert.Equal("unknown", report.Volumes[1].State.Status);
        Assert.Equal("extent_limit", report.Volumes[1].State.Reason);
        Assert.Equal(2, native.QueryCount);
    }

    [Fact]
    public async Task Timeout_does_not_start_a_second_native_operation_while_the_first_is_pending()
    {
        using var release = new ManualResetEventSlim();
        var native = new FakeNative([], (_, _) => throw new InvalidOperationException("No volume query expected."), () =>
        {
            release.Wait();
            return new([], true);
        });
        var reader = new WindowsVolumeDiskTopologyReader(native, TimeProvider.System, TimeSpan.FromMilliseconds(30));

        var first = await reader.ReadAsync();
        var second = await reader.ReadAsync();

        Assert.Equal("unavailable", first.State.Status);
        Assert.Equal("timeout", first.State.Reason);
        Assert.Equal(first.ObservationId, second.ObservationId);
        Assert.Equal(1, native.EnumerationCount);

        release.Set();
    }

    private static string VolumePath() => $@"\\?\Volume{{{Guid.NewGuid():D}}}\";

    private sealed class FakeNative(
        IReadOnlyList<string> paths,
        Func<string, int, VolumeExtentQuery> query,
        Func<NativeVolumeEnumeration>? enumerate = null) : IWindowsVolumeDiskTopologyNativeApi
    {
        private int _enumerationCount;
        private int _queryCount;
        private readonly List<int> _capacities = [];

        public int EnumerationCount => Volatile.Read(ref _enumerationCount);
        public int QueryCount => Volatile.Read(ref _queryCount);
        public IReadOnlyList<int> QueryCapacities => _capacities;

        public NativeVolumeEnumeration EnumerateVolumes(int maximumVolumes)
        {
            Interlocked.Increment(ref _enumerationCount);
            return enumerate?.Invoke() ?? new(paths, paths.Count < maximumVolumes);
        }

        public VolumeExtentQuery QueryExtents(string volumeGuidPath, int extentCapacity)
        {
            Interlocked.Increment(ref _queryCount);
            _capacities.Add(extentCapacity);
            return query(volumeGuidPath, extentCapacity);
        }
    }
}
