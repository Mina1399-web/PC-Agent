using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

[Collection("ReleaseTooling")]
public sealed class ReleaseArchiveTests
{
    [Fact]
    public async Task Archive_keeps_base_directory_literal_paths_empty_directories_and_exact_file_bytes()
    {
        using var fixture = new Fixture();
        var result = await Run(fixture);
        Assert.True(result.Exit == 0, result.Error + result.Output);
        using var archive = ZipFile.OpenRead(fixture.Archive);
        var prefix = Path.GetFileName(fixture.Package) + "/";
        Assert.All(archive.Entries, entry => Assert.StartsWith(prefix, entry.FullName, StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "empty/");
        var files = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        Assert.Equal(new[] { prefix + "a.bin", prefix + "nested/value.txt" }, files.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        foreach (var entry in files)
        {
            var original = Path.Combine(fixture.Package, entry.FullName[prefix.Length..]);
            using var source = File.OpenRead(original);
            using var stored = entry.Open();
            Assert.Equal(SHA256.HashData(source), SHA256.HashData(stored));
            Assert.Equal(new FileInfo(original).Length, entry.Length);
        }
    }

    [Fact]
    public async Task Existing_archive_is_refused_without_overwriting_any_bytes()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Archive, [19, 27, 42]);

        var result = await Run(fixture);

        Assert.NotEqual(0, result.Exit);
        Assert.Contains("release_archive_output_exists", result.Error);
        Assert.Equal(new byte[] { 19, 27, 42 }, File.ReadAllBytes(fixture.Archive));
    }

    [Fact]
    public async Task Archive_inside_source_is_refused_before_creating_a_partial_output()
    {
        using var fixture = new Fixture();
        fixture.Archive = Path.Combine(fixture.Package, "recursive.zip");

        var result = await Run(fixture);

        Assert.NotEqual(0, result.Exit);
        Assert.Contains("release_archive_output_inside_source", result.Error);
        Assert.False(File.Exists(fixture.Archive));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("source-root")]
    [InlineData("destination")]
    public async Task Reparse_source_and_output_parent_are_refused_without_creating_archive(string location)
    {
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "junction-target");
        Directory.CreateDirectory(target);
        var link = location == "source" ? Path.Combine(fixture.Package, "linked") : Path.Combine(fixture.Root, "linked");
        fixture.Junction = link;
        if (location == "source-root") fixture.Package = link;
        if (location == "destination") fixture.Archive = Path.Combine(link, "result.zip");
        var destination = location == "source-root" ? fixture.OriginalPackage : target;
        var before = "New-Item -ItemType Junction -Path '" + Quote(link) + "' -Target '" + Quote(destination) + "' | Out-Null\n";

        var result = await Run(fixture, before);

        Assert.NotEqual(0, result.Exit);
        Assert.Contains("release_archive_reparse_point", result.Error);
        Assert.False(File.Exists(fixture.Archive));
        Assert.False(File.Exists(Path.Combine(target, "result.zip")));
        Assert.True(File.Exists(Path.Combine(fixture.OriginalPackage, "a.bin")));
    }

    private static async Task<(int Exit, string Output, string Error)> Run(Fixture fixture, string before = "")
    {
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var helper = Path.Combine(repository, "tools", "New-ReleaseArchive.ps1");
        var script = Path.Combine(fixture.Root, "probe.ps1");
        File.WriteAllText(script, "$ErrorActionPreference='Stop'\n" + before + "& '" + Quote(helper)
            + "' -PackagePath '" + Quote(fixture.Package) + "' -ArchivePath '" + Quote(fixture.Archive) + "'\n", new UTF8Encoding(false));
        var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_GCHeapHardLimit"] = "08000000"; // 128 MiB child managed heap; parent guards are unchanged.
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string Quote(string path) => path.Replace("'", "''");

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HostAgentArchiveTests", Guid.NewGuid().ToString("N"));
        public string OriginalPackage { get; }
        public string Package { get; set; }
        public string Archive { get; set; }
        public string? Junction { get; set; }
        public Fixture()
        {
            Package = OriginalPackage = Path.Combine(Root, "candidate [literal] with spaces");
            Archive = Path.Combine(Root, "candidate.zip");
            Directory.CreateDirectory(Path.Combine(Package, "nested"));
            Directory.CreateDirectory(Path.Combine(Package, "empty"));
            File.WriteAllBytes(Path.Combine(Package, "a.bin"), [0, 7, 127, 255]);
            File.WriteAllText(Path.Combine(Package, "nested", "value.txt"), "Synthetic archive bytes\n", new UTF8Encoding(false));
        }
        public void Dispose()
        {
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HostAgentArchiveTests")) + Path.DirectorySeparatorChar;
            var actual = Path.GetFullPath(Root);
            if (!actual.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("fixture_cleanup_path_mismatch");
            if (Junction is not null && Directory.Exists(Junction)) Directory.Delete(Junction); // Delete the link, never traverse it.
            Directory.Delete(actual, recursive: true);
        }
    }
}
