using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Windows;
using System.Windows.Markup;

namespace HostAgent.Desktop;

public sealed record InterfaceLanguage(string Code, string NativeName);

public sealed class DesktopLocalization
{
    public static readonly DependencyProperty HeaderKeyProperty=DependencyProperty.RegisterAttached("HeaderKey",typeof(string),typeof(DesktopLocalization));
    public static string? GetHeaderKey(DependencyObject target)=>(string?)target.GetValue(HeaderKeyProperty);
    public static void SetHeaderKey(DependencyObject target,string value)=>target.SetValue(HeaderKeyProperty,value);
    internal static readonly ResourceManager Catalog = new("HostAgent.Desktop.Resources.UiText", typeof(DesktopLocalization).Assembly);
    private readonly IDesktopPreferences _preferences;
    public static IReadOnlyList<InterfaceLanguage> Languages { get; } = [new("cs-CZ", "Čeština"), new("en", "English"), new("de", "Deutsch")];
    public string LanguageCode { get; private set; }
    public CultureInfo Culture => CultureInfo.GetCultureInfo(LanguageCode);
    public bool PreferenceReadFailed { get; }
    public event EventHandler? Changed;

    public DesktopLocalization(IDesktopPreferences preferences, CultureInfo windowsLanguage)
    {
        _preferences = preferences;
        string? stored = null;
        try { stored = preferences.ReadLanguage(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { PreferenceReadFailed = true; }
        LanguageCode = stored is null ? ResolveWindowsLanguage(windowsLanguage) : Languages.Any(x => x.Code == stored) ? stored : "en";
    }

    public static DesktopLocalization ForCurrentUser() => new(DesktopPreferences.ForCurrentUser(), CultureInfo.CurrentUICulture);
    public static string ResolveWindowsLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch { "cs" => "cs-CZ", "de" => "de", "en" => "en", _ => "en" };

    public void SelectLanguage(string code)
    {
        if (!Languages.Any(x => x.Code == code)) throw new ArgumentException("unsupported_ui_language", nameof(code));
        if (code == LanguageCode) return;
        // Publish the new language only after a durable preference write succeeds.
        _preferences.SaveLanguage(code);
        LanguageCode = code;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string Text(string key, params object?[] arguments) => Text(Culture, key, arguments);
    public static string Text(CultureInfo culture, string key, params object?[] arguments)
    {
        var text = Catalog.GetString(key, culture) ?? Catalog.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new ArgumentException("unknown_ui_text: " + key, nameof(key));
        return arguments.Length == 0 ? text : string.Format(culture, text, arguments);
    }

    public void Apply(Window window)
    {
        foreach (DictionaryEntry entry in Catalog.GetResourceSet(CultureInfo.InvariantCulture, true, true)!)
            window.Resources["Loc." + entry.Key] = Catalog.GetString((string)entry.Key, Culture)!;
        window.Language = XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
        // WPF's own control menus also use the dispatcher thread UI culture.
        CultureInfo.CurrentCulture=Culture;
        CultureInfo.CurrentUICulture=Culture;
        ApplyHeaders(window);
    }

    private void ApplyHeaders(FrameworkElement element)
    {
        if(element is System.Windows.Controls.DataGrid grid)
            foreach(var column in grid.Columns)
                if(GetHeaderKey(column) is {} key)column.Header=Catalog.GetString(key,Culture)!;
        foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<FrameworkElement>())ApplyHeaders(child);
    }

    // Code-built windows register their static source text once, before display.
    internal void BindStaticText(FrameworkElement element)
    {
        void Bind(DependencyProperty property)
        {
            if(element.GetValue(property) is string key && Catalog.GetString(key,CultureInfo.InvariantCulture) is not null)
                element.SetResourceReference(property,"Loc."+key);
        }
        if(element is Window)Bind(Window.TitleProperty);
        if(element is System.Windows.Controls.TextBlock)Bind(System.Windows.Controls.TextBlock.TextProperty);
        if(element is System.Windows.Controls.ContentControl)Bind(System.Windows.Controls.ContentControl.ContentProperty);
        Bind(System.Windows.Automation.AutomationProperties.NameProperty);
        foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<FrameworkElement>())BindStaticText(child);
    }
}
