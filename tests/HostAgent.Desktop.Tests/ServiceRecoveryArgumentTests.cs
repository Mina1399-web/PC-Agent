using System;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class ServiceRecoveryArgumentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parser_accepts_only_allowlisted_theme_token(bool darkTheme)
    {
        var id = Guid.NewGuid();
        var args = darkTheme
            ? new[] { "--review-service-crash", id.ToString("N"), "Spooler", "--dark-theme" }
            : new[] { "--review-service-crash", id.ToString("N"), "Spooler" };

        Assert.True(Program.TryParseServiceRecoveryArguments(args, out var parsedId, out var service, out var parsedDark));
        Assert.Equal(id, parsedId);
        Assert.Equal("Spooler", service);
        Assert.Equal(darkTheme, parsedDark);
    }

    [Theory]
    [InlineData("--light-theme")]
    [InlineData("--dark-theme;anything")]
    public void Parser_rejects_unrecognized_theme_arguments(string themeArgument)
    {
        var args = new[] { "--review-service-crash", Guid.NewGuid().ToString("N"), "Spooler", themeArgument };
        Assert.False(Program.TryParseServiceRecoveryArguments(args, out _, out _, out _));
    }
}
