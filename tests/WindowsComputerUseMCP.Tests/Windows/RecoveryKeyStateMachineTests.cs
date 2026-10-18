using WindowsComputerUseMCP.Windows.Services;

namespace WindowsComputerUseMCP.Tests.Windows;

public sealed class RecoveryKeyStateMachineTests
{
    [Theory]
    [InlineData(0x1B)]
    [InlineData(0x24)]
    public void EscAndHomeRecoverOnReleaseAndAreSuppressed(uint key)
    {
        var state = new RecoveryKeyStateMachine();

        var down = state.Process(key, keyDown: true);
        var up = state.Process(key, keyDown: false);

        Assert.False(down.ShouldRecover);
        Assert.True(down.SuppressKey);
        Assert.True(up.ShouldRecover);
        Assert.True(up.SuppressKey);
    }

    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void StandaloneWindowsKeyRecoversOnRelease(uint key)
    {
        var state = new RecoveryKeyStateMachine();

        var down = state.Process(key, keyDown: true);
        var up = state.Process(key, keyDown: false);

        Assert.False(down.ShouldRecover);
        Assert.False(down.SuppressKey);
        Assert.True(up.ShouldRecover);
        Assert.False(up.SuppressKey);
    }

    [Fact]
    public void WindowsKeyUsedAsModifierDoesNotRecover()
    {
        var state = new RecoveryKeyStateMachine();

        state.Process(0x5B, keyDown: true);
        state.Process((uint)'R', keyDown: true);
        var up = state.Process(0x5B, keyDown: false);

        Assert.False(up.ShouldRecover);
        Assert.False(up.SuppressKey);
    }

    [Fact]
    public void WindowsKeyWithOtherModifierDoesNotRecover()
    {
        var state = new RecoveryKeyStateMachine();

        state.Process(0x5B, keyDown: true, anotherModifierIsDown: true);
        var up = state.Process(0x5B, keyDown: false);

        Assert.False(up.ShouldRecover);
        Assert.False(up.SuppressKey);
    }

    [Fact]
    public void WindowsKeyReleasedWhileAnotherModifierIsDownDoesNotRecover()
    {
        var state = new RecoveryKeyStateMachine();

        state.Process(0x5B, keyDown: true);
        var up = state.Process(0x5B, keyDown: false, anotherModifierIsDown: true);

        Assert.False(up.ShouldRecover);
        Assert.False(up.SuppressKey);
    }

    [Fact]
    public void UnrelatedKeyPassesThroughWithoutRecovery()
    {
        var state = new RecoveryKeyStateMachine();

        var action = state.Process((uint)'A', keyDown: true);

        Assert.False(action.ShouldRecover);
        Assert.False(action.SuppressKey);
    }

    [Fact]
    public void RecoveryAgentRejectsAnInvalidTimeoutBeforeStarting()
    {
        var handled = DesktopRecoveryAgent.TryRun(
            ["--wcu-desktop-recovery-agent", "Default", @"Local\WcuRecoveryReady_test", "0"],
            out var exitCode);

        Assert.True(handled);
        Assert.Equal(2, exitCode);
    }
}
