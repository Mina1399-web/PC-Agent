using System;
using System.Windows;
using System.Windows.Controls;

namespace HostAgent.Desktop;

internal static class OrbitTheme
{
    internal static void SetPalette(Window window, bool dark)
    {
        var dictionaries = window.Resources.MergedDictionaries;
        var palette = Load(dark ? "OrbitDark" : "OrbitLight");
        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (IsPalette(dictionaries[index]))
            {
                dictionaries[index] = palette;
                return;
            }
        }

        dictionaries.Insert(0, palette);
    }

    internal static void ApplyToDialog(Window dialog, Window? owner)
    {
        var dark = owner?.Resources.MergedDictionaries is { } dictionaries &&
            System.Linq.Enumerable.Any(dictionaries, dictionary =>
                dictionary.Source?.OriginalString.EndsWith("/OrbitDark.xaml", StringComparison.Ordinal) == true);
        SetPalette(dialog, dark);
        dialog.Resources.MergedDictionaries.Add(Load("OrbitControls"));
        dialog.SetResourceReference(Control.BackgroundProperty, "CanvasBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        dialog.UseLayoutRounding = true;
    }

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString is { } source &&
        (source.EndsWith("/OrbitLight.xaml", StringComparison.Ordinal) ||
         source.EndsWith("/OrbitDark.xaml", StringComparison.Ordinal));

    private static ResourceDictionary Load(string name) => new()
    {
        Source = new Uri($"/HostAgent.Desktop;component/Themes/{name}.xaml", UriKind.Relative)
    };
}
