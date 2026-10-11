namespace HostAgent.Infrastructure.Security;
public sealed record GuardHistory(int IncidentAttempts, int TargetHourAttempts,
    int RestartHourAttempts, DateTimeOffset? LastTargetAttemptUtc);
public sealed record GuardLimits(int MaximumIncidentAttempts = 3, int MaximumTargetHourAttempts = 5,
    int MaximumRestartHourAttempts = 3, int CooldownSeconds = 60);
public sealed class RemediationGuard
{
    public string? Denial(GuardHistory history, GuardLimits limits, bool restart, DateTimeOffset nowUtc)
    {
        if (limits.MaximumIncidentAttempts is < 1 or > 3 || limits.MaximumTargetHourAttempts is < 1 or > 5 ||
            limits.MaximumRestartHourAttempts is < 1 or > 3 || limits.CooldownSeconds is < 60 or > 3600 ||
            history.IncidentAttempts < 0 || history.TargetHourAttempts < 0 || history.RestartHourAttempts < 0)
            return "invalid_guard_configuration";
        if (history.LastTargetAttemptUtc > nowUtc) return "clock_regression";
        if (history.IncidentAttempts >= limits.MaximumIncidentAttempts) return "attempt_limit";
        if (history.TargetHourAttempts >= limits.MaximumTargetHourAttempts) return "rate_limit";
        if (restart && history.RestartHourAttempts >= limits.MaximumRestartHourAttempts) return "restart_loop";
        if (history.LastTargetAttemptUtc is { } last)
        {
            if (nowUtc - last < TimeSpan.FromSeconds(limits.CooldownSeconds)) return "cooldown";
            if (restart && history.RestartHourAttempts > 0 &&
                nowUtc - last < TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Min(history.RestartHourAttempts - 1, 3))))
                return "restart_backoff";
        }
        return null;
    }
}
