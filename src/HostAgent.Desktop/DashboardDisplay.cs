using System;
using System.Globalization;
using System.Windows.Data;

namespace HostAgent.Desktop;

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes < 0) return "—";
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return size.ToString("0.#", culture) + " " + units[unit];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class IncidentStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "Detected" => "Zjištěno", "Active" => "Aktivní", "Acknowledged" => "Vzato na vědomí",
        "RemediationProposed" => "Navržen zásah", "RemediationPendingApproval" => "Čeká na schválení",
        "Remediating" => "Probíhá řešení", "Resolved" => "Vyřešeno", "Failed" => "Zásah selhal",
        "Escalated" => "Vyžaduje pozornost", null => "—", var state => state
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
