namespace HostAgent.Cli;

public static class CliAccessMessages
{
    public const string AdministratorRequired =
        "administrator_required: This CLI command requires an elevated administrator session. " +
        "For read-only monitoring, open Host Agent from Start. For administration, close this window, " +
        "open PowerShell or Windows Terminal using Run as administrator, then retry. No action was performed.";
}
