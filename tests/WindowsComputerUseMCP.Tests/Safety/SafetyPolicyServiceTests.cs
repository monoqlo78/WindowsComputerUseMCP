using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Safety;
using WindowsComputerUseMCP.Tests.TestSupport;

namespace WindowsComputerUseMCP.Tests.Safety;

public class SafetyPolicyServiceTests
{
    private static SafetyPolicyService CreateService(
        WindowsComputerUseMcpOptions? options = null,
        EmergencyStopService? emergencyStop = null)
    {
        options ??= new WindowsComputerUseMcpOptions();
        emergencyStop ??= new EmergencyStopService();
        return new SafetyPolicyService(new TestOptionsMonitor<WindowsComputerUseMcpOptions>(options), emergencyStop);
    }

    [Fact]
    public void Evaluate_AllowsBenignMouseClick_ByDefault()
    {
        var service = CreateService();

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "mouse_click",
            InspectionTexts = ["OK"],
        });

        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresConfirmation);
    }

    [Theory]
    [InlineData("削除")]
    [InlineData("Delete")]
    [InlineData("送信")]
    [InlineData("Send")]
    [InlineData("購入")]
    [InlineData("支払い")]
    [InlineData("公開")]
    [InlineData("上書き保存")]
    public void Evaluate_RequiresConfirmation_ForDangerousActionKeywords(string keyword)
    {
        var service = CreateService();

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "ui_invoke",
            InspectionTexts = [keyword],
        });

        Assert.False(decision.Allowed);
        Assert.True(decision.RequiresConfirmation);
        Assert.NotNull(decision.Category);
    }

    [Fact]
    public void Evaluate_AllowsDangerousAction_WhenCallerAcknowledged()
    {
        var service = CreateService();

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "ui_invoke",
            InspectionTexts = ["削除"],
            CallerAcknowledgedConfirmation = true,
        });

        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresConfirmation);
    }

    [Fact]
    public void Evaluate_Denies_WhenDangerousConfirmationDisabledInSettings()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.RequireConfirmationForDangerousActions = false;
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "ui_invoke",
            InspectionTexts = ["削除"],
        });

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesPasswordFieldInput_ByDefault()
    {
        var service = CreateService();

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "keyboard_type_text",
            IsPasswordField = true,
        });

        Assert.False(decision.Allowed);
        Assert.False(decision.RequiresConfirmation);
        Assert.Equal("PasswordField", decision.Category);
    }

    [Fact]
    public void Evaluate_AllowsPasswordFieldInput_WhenExplicitlyEnabled()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.AllowPasswordFieldInput = true;
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "keyboard_type_text",
            IsPasswordField = true,
        });

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesProtectedSurface()
    {
        var service = CreateService();

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "mouse_click",
            IsProtectedSurface = true,
        });

        Assert.False(decision.Allowed);
        Assert.Equal("ProtectedSurface", decision.Category);
    }

    [Fact]
    public void Evaluate_DeniesAllInputTools_WhenEmergencyStopActive()
    {
        var emergencyStop = new EmergencyStopService();
        emergencyStop.Activate("test");
        var service = CreateService(emergencyStop: emergencyStop);

        var decision = service.Evaluate(new SafetyCheckRequest { ToolName = "mouse_click" });

        Assert.False(decision.Allowed);
        Assert.Equal("EmergencyStop", decision.Category);
    }

    [Fact]
    public void Evaluate_AllowsReadOnlyTools_WhenEmergencyStopActive()
    {
        var emergencyStop = new EmergencyStopService();
        emergencyStop.Activate("test");
        var service = CreateService(emergencyStop: emergencyStop);

        var decision = service.Evaluate(new SafetyCheckRequest { ToolName = "window_list" });

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Evaluate_DeniesDeniedProcess()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.DeniedProcesses.Add("malicious.exe");
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "mouse_click",
            ProcessName = "malicious.exe",
        });

        Assert.False(decision.Allowed);
        Assert.Equal("DeniedProcess", decision.Category);
    }

    [Fact]
    public void Evaluate_DeniesProcessNotInAllowList_WhenAllowListNonEmpty()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.AllowedProcesses.Add("notepad.exe");
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest
        {
            ToolName = "mouse_click",
            ProcessName = "calculator.exe",
        });

        Assert.False(decision.Allowed);
        Assert.Equal("NotAllowedProcess", decision.Category);
    }

    [Fact]
    public void Evaluate_DeniesMouseClicks_WhenDisabledGlobally()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.AllowMouseClicks = false;
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest { ToolName = "mouse_click" });

        Assert.False(decision.Allowed);
        Assert.Equal("MouseDisabled", decision.Category);
    }

    [Fact]
    public void Evaluate_DeniesKeyboardInput_WhenDisabledGlobally()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.AllowKeyboardInput = false;
        var service = CreateService(options);

        var decision = service.Evaluate(new SafetyCheckRequest { ToolName = "keyboard_press" });

        Assert.False(decision.Allowed);
        Assert.Equal("KeyboardDisabled", decision.Category);
    }

    [Fact]
    public void CheckRateLimit_DeniesAfterExceedingMaxConsecutiveOperations()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.MaxConsecutiveOperations = 3;
        options.Safety.RateLimitWindowSeconds = 60;
        var service = CreateService(options);

        Assert.True(service.CheckRateLimit("mouse_click").Allowed);
        Assert.True(service.CheckRateLimit("mouse_click").Allowed);
        Assert.True(service.CheckRateLimit("mouse_click").Allowed);
        var fourth = service.CheckRateLimit("mouse_click");

        Assert.False(fourth.Allowed);
        Assert.Equal("RateLimitCount", fourth.Category);
    }

    [Fact]
    public void CheckRateLimit_TracksToolsIndependently()
    {
        var options = new WindowsComputerUseMcpOptions();
        options.Safety.MaxConsecutiveOperations = 1;
        options.Safety.RateLimitWindowSeconds = 60;
        var service = CreateService(options);

        Assert.True(service.CheckRateLimit("mouse_click").Allowed);
        Assert.True(service.CheckRateLimit("keyboard_press").Allowed);
        Assert.False(service.CheckRateLimit("mouse_click").Allowed);
    }
}
