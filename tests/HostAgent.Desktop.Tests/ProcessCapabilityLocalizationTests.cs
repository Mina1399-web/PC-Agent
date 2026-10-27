using System.Globalization;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class ProcessCapabilityLocalizationTests
{
    [Theory]
    [InlineData("cs-CZ", "Omezená identita některých procesů", "Přesná identita některých procesů není dostupná; před zásahem je nutné ji samostatně ověřit.")]
    [InlineData("en", "Limited identity for some processes", "Exact identity is unavailable for some processes; verify it separately before any process action.")]
    [InlineData("de", "Identität einiger Prozesse eingeschränkt", "Die genaue Identität einiger Prozesse ist nicht verfügbar; vor einer Prozessaktion muss sie separat überprüft werden.")]
    public void Counter_only_identity_limit_is_explained_in_every_supported_language(string language, string title, string reason)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Assert.Equal(title, TechnicalText.Format("optional_process_identity_unavailable", culture));
        Assert.Equal(reason, TechnicalText.Format("process_identity_unavailable", culture));
    }
}
