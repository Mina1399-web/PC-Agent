namespace HostAgent.Desktop;

internal static class DesktopModePolicy
{
    public static bool CanOpenDeveloperSurface(DesktopMode mode) => mode == DesktopMode.Developer;

    public static bool CanNavigateTo(int index, DesktopMode mode)
        => !IsDeveloperTab(index) || CanOpenDeveloperSurface(mode);

    public static bool CanChangeMode(bool childWorkflowPending,
        bool guidedActionRunning, bool maintenanceWindowOpen)
        => !(childWorkflowPending || guidedActionRunning || maintenanceWindowOpen);

    // Guided Windows repair remains developer-only. File maintenance has a
    // separate Standard Temp scope with its own service authorization checks.
    public static bool IsDeveloperTab(int index) => index == 4;
}
