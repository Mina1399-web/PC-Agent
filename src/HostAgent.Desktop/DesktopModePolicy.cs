namespace HostAgent.Desktop;

public enum DesktopRoute { Situation, Events, Recommendations, System, Maintenance, WindowsTools }

internal static class DesktopModePolicy
{
    public static bool CanOpenDeveloperSurface(DesktopMode mode) => mode == DesktopMode.Developer;

    public static bool CanNavigateTo(DesktopRoute? route, DesktopMode mode) => route switch
    {
        DesktopRoute.Situation or DesktopRoute.Events or DesktopRoute.Recommendations
            or DesktopRoute.System or DesktopRoute.WindowsTools => true,
        DesktopRoute.Maintenance => CanOpenDeveloperSurface(mode),
        _ => false
    };

    public static bool CanChangeMode(bool childWorkflowPending,
        bool guidedActionRunning, bool maintenanceWindowOpen)
        => !(childWorkflowPending || guidedActionRunning || maintenanceWindowOpen);

    // Guided Windows repair remains developer-only. File maintenance has a
    // separate Standard Temp scope with its own service authorization checks.
    public static bool IsDeveloperTab(DesktopRoute route) => route == DesktopRoute.Maintenance;
}
