namespace HostAgent.Infrastructure.Tests;

internal static class ChaosFixtureRetention
{
    private static bool Retain(string path)
    {
        var root=Environment.GetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT");
        if(string.IsNullOrWhiteSpace(root)) return false;
        var full=Path.GetFullPath(path);
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return full.Equals(root,StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    internal static void DeleteFile(string path) { if(!Retain(path))File.Delete(path); }
    internal static void DeleteDirectory(string path) { if(!Retain(path))Directory.Delete(path, true); }
}
