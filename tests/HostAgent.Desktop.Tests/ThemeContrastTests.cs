using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class ThemeContrastTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(0, 0, 1280, 760, 8, 8, 1264, 744)]
    [InlineData(100, 50, 850, 600, 108, 58, 834, 584)]
    public void Initial_window_bounds_fit_inside_the_available_work_area(
        double x, double y, double workWidth, double workHeight,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var bounds = MainWindow.ClampInitialBounds(new Rect(x, y, workWidth, workHeight));

        Assert.Equal(expectedX, bounds.Left);
        Assert.Equal(expectedY, bounds.Top);
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
        Assert.True(bounds.Right <= x + workWidth);
        Assert.True(bounds.Bottom <= y + workHeight);
    }

    [Fact]
    public void Chart_accessible_name_does_not_assume_series_colors_from_one_theme()
    {
        var root = FindProjectRoot();
        foreach (var file in new[] { "UiText.resx", "UiText.cs-CZ.resx", "UiText.de.resx" })
        {
            var resources = XDocument.Load(Path.Combine(root, "src", "HostAgent.Desktop", "Resources", file));
            var entry = resources.Root!.Elements("data")
                .Single(item => (string?)item.Attribute("name") == "CPUAndRAMHistoryOver24HoursSavedPoints");
            var value = entry.Element("value")!.Value.ToLowerInvariant();
            foreach (var color in new[] { "blue", "purple", "modře", "fialově", "blau", "violett" })
                Assert.DoesNotContain(color, value);
        }

        var code = File.ReadAllText(Path.Combine(root, "src", "HostAgent.Desktop", "MainWindow.xaml.cs"));
        Assert.Contains("CPUAndRAMHistoryOver24HoursSavedPoints", code);
        Assert.DoesNotContain("CPUAndRAMHistoryOver24HoursSavedPointsCPUInBlueRAMInPurple", code);
    }

    [Theory]
    [InlineData("OrbitLight.xaml")]
    [InlineData("OrbitDark.xaml")]
    public void Selected_grid_text_has_readable_contrast_for_row_cell_and_focus(string paletteFile)
    {
        var root = FindProjectRoot();
        var palette = XDocument.Load(Path.Combine(root, "src", "HostAgent.Desktop", "Themes", paletteFile));
        var selected = Brush(palette, "SelectedBrush");
        var text = Brush(palette, "SelectedInkBrush");
        Assert.True(Contrast(selected, text) >= 4.5, $"{paletteFile}: selected row text contrast was {Contrast(selected, text):F2}:1.");

        var controls = XDocument.Load(Path.Combine(root, "src", "HostAgent.Desktop", "Themes", "OrbitControls.xaml"));
        var row = Style(controls, "DataGridRow");
        var cell = Style(controls, "DataGridCell");
        AssertForeground(row, "IsSelected");
        AssertForeground(cell, "IsSelected");
        AssertForeground(cell, "IsKeyboardFocusWithin");
    }

    private static XElement Style(XDocument document, string targetType) => document.Root!
        .Elements(Presentation + "Style")
        .Single(style => (string?)style.Attribute("TargetType") == targetType);

    private static void AssertForeground(XElement style, string triggerProperty)
    {
        var trigger = style.Descendants(Presentation + "Trigger")
            .Single(item => (string?)item.Attribute("Property") == triggerProperty);
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "{DynamicResource SelectedInkBrush}");
    }

    private static string Brush(XDocument palette, string key) => (string)palette.Root!
        .Elements(Presentation + "SolidColorBrush")
        .Single(item => (string?)item.Attribute(Xaml + "Key") == key)
        .Attribute("Color")!;

    private static double Contrast(string background, string foreground)
    {
        var first = Luminance(background);
        var second = Luminance(foreground);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string color)
    {
        var channels = Enumerable.Range(0, 3)
            .Select(index => Convert.ToInt32(color.Substring(1 + index * 2, 2), 16) / 255d)
            .Select(value => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4))
            .ToArray();
        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    }

    private static string FindProjectRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "HostAgent.Desktop", "Themes", "OrbitControls.xaml")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the source tree for shared theme contrast checks.");
    }
}

