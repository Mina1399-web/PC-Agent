namespace HostAgent.Infrastructure.Tests;

internal static class TestDotNetHost
{
    public static string Resolve()
    {
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var dotnet = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", "dotnet.exe"));
        return File.Exists(dotnet) ? dotnet : "dotnet";
    }
}
