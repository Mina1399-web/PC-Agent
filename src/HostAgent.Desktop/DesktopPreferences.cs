using System;
using System.IO;
using System.Text.Json.Nodes;

namespace HostAgent.Desktop;

public interface IDesktopPreferences
{
    string? ReadLanguage();
    void SaveLanguage(string language);
}

/// <summary>User-scoped JSON settings. Never reads or writes service configuration.</summary>
public sealed class DesktopPreferences(string path) : IDesktopPreferences
{
    private const int MaximumBytes = 64 * 1024;
    public static DesktopPreferences ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HostAgent", "desktop.json"));

    public string? ReadLanguage() => ReadObject()?["uiLanguage"]?.GetValue<string>();

    public void SaveLanguage(string language)
    {
        if (language is not ("cs-CZ" or "en" or "de")) throw new ArgumentException("unsupported_ui_language", nameof(language));
        var fullPath = Path.GetFullPath(path);
        AssertPlainPath(fullPath);
        var settings = ReadObject() ?? new JsonObject();
        settings["uiLanguage"] = language;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        AssertPlainPath(fullPath);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(settings);
                if(bytes.Length>MaximumBytes)throw new InvalidDataException("desktop_settings_too_large");
                output.Write(bytes);
                output.Flush(true);
            }
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private JsonObject? ReadObject()
    {
        var fullPath = Path.GetFullPath(path);
        AssertPlainPath(fullPath);
        if (!File.Exists(fullPath)) return null;
        using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaximumBytes) throw new InvalidDataException("desktop_settings_too_large");
        return JsonNode.Parse(input) as JsonObject ?? throw new InvalidDataException("desktop_settings_invalid");
    }

    private static void AssertPlainPath(string fullPath)
    {
        for (var item = fullPath; item is not null; item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("desktop_settings_reparse_point");
    }
}
