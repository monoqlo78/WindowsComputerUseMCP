using WindowsComputerUseMCP.Windows.Services;

namespace WindowsComputerUseMCP.Tests.Windows;

public sealed class TemporaryDesktopRecoveryStateTests
{
    [Fact]
    public void TimeoutStartsWhenTargetActuallyBecomesVisible()
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 2);

        Assert.Equal(TemporaryRecoveryDecision.Wait, state.Observe("Default", TimeSpan.FromSeconds(4)));
        Assert.Equal(TemporaryRecoveryDecision.Listen, state.Observe("work", TimeSpan.FromSeconds(5)));
        Assert.Equal(TemporaryRecoveryDecision.Listen, state.Observe("work", TimeSpan.FromSeconds(6)));
        Assert.Equal(TemporaryRecoveryDecision.Recover, state.Observe("work", TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void HelperThatNeverSeesTargetExitsWithoutSwitching()
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 15);

        Assert.Equal(TemporaryRecoveryDecision.Failed, state.Observe("Default", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TargetAppearingAfterStartupDeadlineDoesNotArmRecovery()
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 15);

        Assert.Equal(TemporaryRecoveryDecision.Failed, state.Observe("work", TimeSpan.FromSeconds(11)));
    }

    [Fact]
    public void CompletedMonitoringDoesNotRearmOnLaterTarget()
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 2);
        state.Observe("work", TimeSpan.Zero);
        state.Observe("Default", TimeSpan.FromSeconds(1));

        Assert.Equal(TemporaryRecoveryDecision.Completed, state.Observe("work", TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("another-desktop")]
    public void LeavingTargetEndsMonitoringWithoutSwitchingBack(string desktop)
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 2);
        state.Observe("work", TimeSpan.Zero);

        Assert.Equal(TemporaryRecoveryDecision.Completed, state.Observe(desktop, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Winlogon")]
    [InlineData("Disconnect")]
    [InlineData("Screen-saver")]
    public void InaccessibleOrSecureDesktopIsNeverSwitched(string? desktop)
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 2);
        state.Observe("work", TimeSpan.Zero);

        Assert.Equal(TemporaryRecoveryDecision.Wait, state.Observe(desktop, TimeSpan.FromSeconds(2)));
        Assert.Equal(TemporaryRecoveryDecision.Failed, state.Observe(desktop, TimeSpan.FromSeconds(7)));
    }

    [Fact]
    public void RecoveryCanResumeAfterOrdinaryDesktopBecomesAccessibleAgain()
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", 2);
        state.Observe("work", TimeSpan.Zero);
        state.Observe(null, TimeSpan.FromSeconds(2));

        Assert.Equal(TemporaryRecoveryDecision.Recover, state.Observe("work", TimeSpan.FromSeconds(3)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void TimeoutOutsideDocumentedRangeIsRejected(int seconds)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new TemporaryDesktopRecoveryState("Default", "work", seconds));

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void TimeoutBoundariesHaveFiveSecondsOfBoundedRetries(int seconds)
    {
        var state = new TemporaryDesktopRecoveryState("Default", "work", seconds);
        Assert.Equal(TemporaryRecoveryDecision.Listen, state.Observe("work", TimeSpan.Zero));
        Assert.Equal(TemporaryRecoveryDecision.Recover, state.Observe("work", TimeSpan.FromSeconds(seconds)));
        Assert.Equal(TemporaryRecoveryDecision.Failed, state.Observe("work", TimeSpan.FromSeconds(seconds + 5)));
    }

    [Theory]
    [InlineData("Default", "default")]
    [InlineData("Winlogon", "work")]
    [InlineData("Default", "Disconnect")]
    [InlineData("Default", @"WinSta0\work")]
    public void SameOrReservedDesktopIsRejected(string source, string target)
        => Assert.Throws<ArgumentException>(() => new TemporaryDesktopRecoveryState(source, target, 15));
}
