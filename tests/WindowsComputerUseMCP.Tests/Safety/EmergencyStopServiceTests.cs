using WindowsComputerUseMCP.Safety;

namespace WindowsComputerUseMCP.Tests.Safety;

public class EmergencyStopServiceTests
{
    [Fact]
    public void IsActive_DefaultsToFalse()
    {
        var service = new EmergencyStopService();

        Assert.False(service.IsActive);
    }

    [Fact]
    public void Activate_SetsIsActiveTrue_AndRaisesEvent()
    {
        var service = new EmergencyStopService();
        var raised = new List<bool>();
        service.StateChanged += (_, active) => raised.Add(active);

        service.Activate("test");

        Assert.True(service.IsActive);
        Assert.Equal([true], raised);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse_AndRaisesEvent()
    {
        var service = new EmergencyStopService();
        service.Activate("test");
        var raised = new List<bool>();
        service.StateChanged += (_, active) => raised.Add(active);

        service.Deactivate();

        Assert.False(service.IsActive);
        Assert.Equal([false], raised);
    }

    [Fact]
    public void Activate_Twice_DoesNotRaiseEventAgain()
    {
        var service = new EmergencyStopService();
        service.Activate("first");
        var raised = new List<bool>();
        service.StateChanged += (_, active) => raised.Add(active);

        service.Activate("second");

        Assert.Empty(raised);
    }
}
