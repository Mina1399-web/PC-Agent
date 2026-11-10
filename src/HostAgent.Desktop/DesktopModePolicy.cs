namespace HostAgent.Desktop;

internal static class DesktopModePolicy
{
    public static bool CanOpenDeveloperSurface(DesktopMode mode) => mode == DesktopMode.Developer;

    public static bool CanNavigateTo(int index, DesktopMode mode)
        => !IsDeveloperTab(index) || CanOpenDeveloperSurface(mode);

    public static bool CanChangeMode(bool childWorkflowPending,
        bool guidedActionRunning, bool maintenanceWindowOpen)
        => !(childWorkflowPending || guidedActionRunning || maintenanceWindowOpen);

    // Maintenance remains developer-only. Windows inventory is useful as a
    // read-only surface in Standard; each mutating entry point is separately gated.
    public static bool IsDeveloperTab(int index) => index == 4;
}
