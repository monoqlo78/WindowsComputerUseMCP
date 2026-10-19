using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Services;

namespace WindowsComputerUseMCP.Tests.Windows;

public sealed class BackgroundDesktopRecoveryValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task InvalidSwitchTimeoutDoesNotStartAHelperOrSwitch(int seconds)
    {
        using var service = new BackgroundDesktopService();

        var result = await service.SwitchAsync(new BackgroundDesktopSwitchRequest
        {
            DesktopName = "work",
            AutoReturnSeconds = seconds,
        });

        Assert.False(result.Executed);
        Assert.Contains("autoReturnSeconds", result.Reason);
    }

    [Theory]
    [InlineData("Winlogon")]
    [InlineData("Disconnect")]
    [InlineData(@"WinSta0\work")]
    public async Task ReservedOrInvalidNamesAreRejectedBeforeNativeOperations(string name)
    {
        using var service = new BackgroundDesktopService();

        var switchResult = await service.SwitchAsync(new BackgroundDesktopSwitchRequest { DesktopName = name });
        var recoverResult = await service.RecoverAsync(name);

        Assert.False(switchResult.Executed);
        Assert.False(recoverResult.Executed);
    }
}
