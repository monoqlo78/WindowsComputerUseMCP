using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsComputerUseMCP.ControlPanel.Services;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Safety;
using WindowsComputerUseMCP.Server.Services;
using WindowsComputerUseMCP.Skills.Figma;

namespace WindowsComputerUseMCP.Tests.Ipc;

public class ControlPanelIpcTests
{
    private static string NewPrefix() => $"WCU-Test-{Guid.NewGuid():N}.";

    [Fact]
    public void FigmaListener_IsDisabledByDefault()
    {
        var options = new WindowsComputerUseMcpOptions();
        Assert.False(options.BackgroundServices.EnableFigmaBridge);
    }

    [Fact]
    public async Task DisabledFigmaBridge_ExplainsOptInWithoutOpeningListener()
    {
        var bridge = new FigmaBridgeServer(NullLogger<FigmaBridgeServer>.Instance);
        Assert.False(bridge.IsListening);
        var error = await Assert.ThrowsAsync<FigmaBridgeException>(() => bridge.SendCommandAsync("get_document_info"));
        Assert.Contains("EnableFigmaBridge=true", error.Message);
        Assert.False(bridge.IsListening);
    }

    [Fact]
    public async Task MultipleServers_StatusAndCommandsReachEveryInstance()
    {
        if (!OperatingSystem.IsWindows()) return;
        var prefix = NewPrefix();
        var first = new EmergencyStopService();
        var second = new EmergencyStopService();
        first.Activate("initial");
        var logger = new RecordingLogger();
        using var server1 = new ControlPanelIpcServer(first, logger, prefix + "1");
        using var server2 = new ControlPanelIpcServer(second, logger, prefix + "2");
        await server1.StartAsync(default);
        await server2.StartAsync(default);
        try
        {
            await WaitForPipeAsync(prefix + "1");
            await WaitForPipeAsync(prefix + "2");
            var client = new ServerIpcClient(prefix);
            var mixed = await client.SendAsync(IpcCommand.Status);
            Assert.True(mixed!.Success);
            Assert.True(mixed.EmergencyStopActive);
            Assert.False(second.IsActive);

            for (var i = 0; i < 30; i++)
            {
                var activated = await client.SendAsync(IpcCommand.Activate, "test");
                Assert.True(activated is not null && activated.Success, string.Join("\n", logger.Messages));
                Assert.StartsWith("2 ", activated.Message);
                Assert.True(first.IsActive);
                Assert.True(second.IsActive);

                var resumed = await client.SendAsync(IpcCommand.Deactivate);
                Assert.True(resumed!.Success);
                Assert.StartsWith("2 ", resumed.Message);
                Assert.False(resumed.EmergencyStopActive);
                Assert.False(first.IsActive);
                Assert.False(second.IsActive);
            }
        }
        finally
        {
            await server1.StopAsync(default);
            await server2.StopAsync(default);
        }
    }

    [Fact]
    public async Task NoServers_ReturnsDisconnectedWithoutStartingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(await new ServerIpcClient(NewPrefix()).SendAsync(IpcCommand.Status));
    }

    [Fact]
    public async Task BusyPipe_LogsOnceAndStopsInsteadOfRetryingForever()
    {
        var name = NewPrefix() + "busy";
        await using var occupied = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var logger = new RecordingLogger();
        using var server = new ControlPanelIpcServer(new EmergencyStopService(), logger, name);
        await server.StartAsync(default);
        await server.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(logger.Levels);
        Assert.Equal(LogLevel.Error, logger.Levels.Single());
        await server.StopAsync(default);
    }

    [Fact]
    public async Task StalledPeer_IsBoundedAndDoesNotHideSuccessfulEmergencyStop()
    {
        if (!OperatingSystem.IsWindows()) return;
        var prefix = NewPrefix();
        var emergencyStop = new EmergencyStopService();
        using var healthy = CreateServer(emergencyStop, prefix + "healthy");
        await using var stalled = new NamedPipeServerStream(prefix + "stalled",
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await healthy.StartAsync(default);
        try
        {
            await WaitForPipeAsync(prefix + "healthy");
            var accepting = stalled.WaitForConnectionAsync();
            var response = await new ServerIpcClient(prefix).SendAsync(IpcCommand.Activate)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await accepting;
            Assert.False(response!.Success);
            Assert.True(response.EmergencyStopActive);
            Assert.True(emergencyStop.IsActive);
        }
        finally
        {
            await healthy.StopAsync(default);
        }
    }

    [Fact]
    public async Task IdleClient_IsDisconnectedAndNextClientCanGetStatus()
    {
        var name = NewPrefix() + "timeout";
        using var server = CreateServer(new EmergencyStopService(), name);
        await server.StartAsync(default);
        try
        {
            await using var idle = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await idle.ConnectAsync(2000);
            await Task.Delay(IpcConstants.RequestTimeoutMs + 250);
            var response = await RequestAsync(name, new IpcRequest { Command = IpcCommand.Status });
            Assert.True(response.Success);
        }
        finally
        {
            await server.StopAsync(default);
        }
    }

    [Fact]
    public async Task InvalidCommandAndMalformedJson_FailWithoutChangingState()
    {
        var name = NewPrefix() + "invalid";
        var emergencyStop = new EmergencyStopService();
        using var server = CreateServer(emergencyStop, name);
        await server.StartAsync(default);
        try
        {
            Assert.False((await RequestAsync(name, new IpcRequest { Command = (IpcCommand)999 })).Success);
            Assert.False((await RequestLineAsync(name, "{broken")).Success);
            Assert.False(emergencyStop.IsActive);
            Assert.True((await RequestAsync(name, new IpcRequest { Command = IpcCommand.Status })).Success);
        }
        finally
        {
            await server.StopAsync(default);
        }
    }

    [Fact]
    public async Task DelayedReader_ReceivesResponseBeforeServerDisconnects()
    {
        var name = NewPrefix() + "delayed";
        using var server = CreateServer(new EmergencyStopService(), name);
        await server.StartAsync(default);
        try
        {
            var response = await RequestLineAsync(name,
                JsonSerializer.Serialize(new IpcRequest { Command = IpcCommand.Status }), 250);
            Assert.True(response.Success);
        }
        finally
        {
            await server.StopAsync(default);
        }
    }

    [Fact]
    public async Task StopAsync_CancelsConnectionWaitPromptly()
    {
        using var server = CreateServer(new EmergencyStopService(), NewPrefix() + "cancel");
        await server.StartAsync(default);
        await server.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(server.ExecuteTask!.IsCompleted);
    }

    private static ControlPanelIpcServer CreateServer(EmergencyStopService emergencyStop, string name) =>
        new(emergencyStop, NullLogger<ControlPanelIpcServer>.Instance, name);

    private static async Task<IpcResponse> RequestAsync(string name, IpcRequest request) =>
        await RequestLineAsync(name, JsonSerializer.Serialize(request));

    private static async Task<IpcResponse> RequestLineAsync(string name, string line, int responseReadDelayMs = 0)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(line.AsMemory(), timeout.Token);
        if (responseReadDelayMs > 0)
        {
            await Task.Delay(responseReadDelayMs, timeout.Token);
        }
        return JsonSerializer.Deserialize<IpcResponse>((await reader.ReadLineAsync(timeout.Token))!)!;
    }

    private static async Task WaitForPipeAsync(string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!Directory.GetFiles(@"\\.\pipe\").Any(path => Path.GetFileName(path) == name))
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class RecordingLogger : ILogger<ControlPanelIpcServer>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Enqueue(logLevel);
            Messages.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
