namespace HostAgent.Desktop;

internal static class DesktopModePolicy
{
    public static bool CanOpenDeveloperSurface(DesktopMode mode) => mode == DesktopMode.Developer;

    public static bool CanChangeMode(bool childWorkflowPending,
        bool guidedActionRunning, bool maintenanceWindowOpen)
        => !(childWorkflowPending || guidedActionRunning || maintenanceWindowOpen);

    public static bool IsDeveloperTab(int index) => index is 4 or 5;
}
