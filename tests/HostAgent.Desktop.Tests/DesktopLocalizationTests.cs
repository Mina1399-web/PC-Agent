using System.Text;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.Json;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DesktopLocalizationTests
{
    [Theory]
    [InlineData("cs-CZ","cs-CZ")]
    [InlineData("cs","cs-CZ")]
    [InlineData("en-GB","en")]
    [InlineData("de-AT","de")]
    [InlineData("fr-FR","en")]
    [InlineData("ja-JP","en")]
    public void First_run_uses_supported_Windows_family_or_English(string windows,string expected)
    {
        using var fixture=new PreferencesFixture();
        var language=new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo(windows));
        Assert.Equal(expected,language.LanguageCode);
        Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public void Selection_survives_restart_and_only_updates_the_user_language_field()
    {
        using var fixture=new PreferencesFixture();
        File.WriteAllText(fixture.Path,"{\"futurePreference\":{\"keep\":true},\"uiLanguage\":\"cs-CZ\"}");
        var language=new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("cs-CZ",language.LanguageCode);
        var changed=0;language.Changed+=(_,_)=>changed++;
        language.SelectLanguage("de");
        Assert.Equal(1,changed);
        var restarted=new DesktopLocalization(new DesktopPreferences(fixture.Path),CultureInfo.GetCultureInfo("cs-CZ"));
        Assert.Equal("de",restarted.LanguageCode);
        using var json=JsonDocument.Parse(File.ReadAllText(fixture.Path));
        Assert.True(json.RootElement.GetProperty("futurePreference").GetProperty("keep").GetBoolean());
        Assert.Equal(2,json.RootElement.EnumerateObject().Count());
        Assert.Single(Directory.GetFiles(fixture.Directory));
        Assert.Throws<ArgumentException>(()=>language.SelectLanguage("fr"));
        Assert.Equal("de",restarted.LanguageCode);
    }

    [Fact]
    public void Failed_save_does_not_switch_or_notify_and_damaged_settings_are_preserved()
    {
        var language=new DesktopLocalization(new UnwritablePreferences(),CultureInfo.GetCultureInfo("cs-CZ"));
        var changed=0;language.Changed+=(_,_)=>changed++;
        Assert.Throws<IOException>(()=>language.SelectLanguage("de"));
        Assert.Equal("cs-CZ",language.LanguageCode);Assert.Equal(0,changed);
        using var fixture=new PreferencesFixture();
        File.WriteAllText(fixture.Path,"{broken");
        var damaged=new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("en"));
        Assert.True(damaged.PreferenceReadFailed);Assert.Equal("en",damaged.LanguageCode);
        Assert.ThrowsAny<JsonException>(()=>damaged.SelectLanguage("de"));
        Assert.Equal("{broken",File.ReadAllText(fixture.Path));Assert.Equal("en",damaged.LanguageCode);
    }

    [Fact]
    public void Unsupported_stored_language_uses_English()
    {
        using var fixture=new PreferencesFixture();File.WriteAllText(fixture.Path,"{\"uiLanguage\":\"fr\"}");
        Assert.Equal("en",new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("cs-CZ")).LanguageCode);
    }

    [Theory]
    [InlineData("{\"uiLanguage\":\"en\",\"uiLanguage\":\"de\"}")]
    [InlineData("{\"future\":1,\"future\":2,\"uiLanguage\":\"de\"}")]
    public void Duplicate_settings_fall_back_without_crashing_or_overwriting(string json)
    {
        using var fixture=new PreferencesFixture();File.WriteAllText(fixture.Path,json);
        var language=new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("en"));
        Assert.True(language.PreferenceReadFailed);Assert.Equal("en",language.LanguageCode);
        Assert.Throws<ArgumentException>(()=>language.SelectLanguage("de"));
        Assert.Equal(json,File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void All_catalogs_have_complete_matching_format_contracts()
    {
        var neutral=DesktopLocalization.Catalog.GetResourceSet(CultureInfo.InvariantCulture,true,false)!;
        foreach(var culture in new[]{"cs-CZ","en","de"})
        {
            var set=DesktopLocalization.Catalog.GetResourceSet(culture=="en"?CultureInfo.InvariantCulture:CultureInfo.GetCultureInfo(culture),true,false)!;
            Assert.NotNull(set);
            foreach(System.Collections.DictionaryEntry entry in neutral)
            {
                var translated=Assert.IsType<string>(set.GetString((string)entry.Key));
                Assert.False(string.IsNullOrWhiteSpace(translated));
                Assert.Equal(CompositeFormat.Parse((string)entry.Value!).MinimumArgumentCount,CompositeFormat.Parse(translated).MinimumArgumentCount);
            }
        }
    }

    [Theory]
    [InlineData("en","1.5 GiB")]
    [InlineData("de","1,5 GiB")]
    public void Display_values_follow_the_selected_culture(string culture,string expected)
        => Assert.Equal(expected,new ByteSizeConverter().Convert(1610612736L,typeof(string),null!,CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void All_recommendations_translate_from_semantic_values_and_preserve_the_action_contract()
    {
        var language=TestLocalization.Czech();var now=DateTimeOffset.UtcNow;
        var samples=Enumerable.Range(0,5).Select(i=>new HostAgent.Core.Dashboard.ViewMetric(now.AddMinutes(-4+i),95)).ToArray();
        var incident=new HostAgent.Core.Dashboard.ViewIncident(Guid.NewGuid().ToString("N"),"Active","windows_bugcheck",80,"High",now,1);
        var snapshot=new HostAgent.Core.Dashboard.DashboardSnapshot(now,"Shadow",true,false,null,null,null,[new("C:",1500,100000,now)],[],[],1,[incident],[],samples,samples);
        var cards=new HostAgent.Core.Dashboard.RecommendationEngine().Evaluate(snapshot,now);Assert.Equal(4,cards.Count);
        foreach(var culture in new[]{"en","de"})
        {
            language.SelectLanguage(culture);
            foreach(var card in cards)
            {
                var text=RecommendationText.Localize(card,snapshot,language);
                Assert.NotEqual(card.Title,text.Title);Assert.NotEqual(card.Benefit,text.Benefit);
                Assert.Equal(card.Code,text.Code);Assert.Equal(card.Action,text.Action);Assert.Equal(card.IncidentId,text.IncidentId);
                if(card.Code.StartsWith("disk_space:",StringComparison.Ordinal))Assert.Contains(culture=="en"?"1.5":"1,5",text.Evidence);
            }
            foreach(var source in TechnicalText.Sources)Assert.False(string.IsNullOrWhiteSpace(language.Text(source)));
        }
    }

    private sealed class UnwritablePreferences:IDesktopPreferences
    {
        public string? ReadLanguage()=>null;
        public void SaveLanguage(string language)=>throw new IOException("synthetic_readonly_fixture");
    }
    internal sealed class PreferencesFixture:IDisposable
    {
        public string Directory{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"HostAgent-Language-"+Guid.NewGuid().ToString("N"));
        public string Path=>System.IO.Path.Combine(Directory,"desktop.json");
        public DesktopPreferences Preferences=>new(Path);
        public PreferencesFixture()=>System.IO.Directory.CreateDirectory(Directory);
        public void Dispose()=>System.IO.Directory.Delete(Directory,true);
    }
}

internal static class TestLocalization
{
    internal static DesktopLocalization Czech()=>new(new MemoryPreferences(),CultureInfo.GetCultureInfo("cs-CZ"));
    private sealed class MemoryPreferences:IDesktopPreferences
    {
        public string? ReadLanguage()=>null;
        public void SaveLanguage(string language){}
    }
}
