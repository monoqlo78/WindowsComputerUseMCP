namespace WindowsComputerUseMCP.Core.Configuration;

/// <summary>ControlPanel (WPF) と Server (MCP stdio プロセス) 間の名前付きパイプ IPC に関する定数。</summary>
public static class IpcConstants
{
    /// <summary>旧固定名パイプ。新サーバーはこの名前に Windows セッションIDとPIDを付加する。</summary>
    public const string PipeName = "WindowsComputerUseMCP.ControlPanel.v1";

    public static string SessionPipePrefix { get; } = GetSessionPipePrefix();

    public static string ProcessPipeName => $"{SessionPipePrefix}{Environment.ProcessId}";

    private static string GetSessionPipePrefix()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"{PipeName}.{process.SessionId}.";
    }

    /// <summary>ControlPanel からの接続試行タイムアウト（ミリ秒）。Server が未起動の場合に速やかに諦めるための値。</summary>
    public const int ClientConnectTimeoutMs = 500;

    public const int RequestTimeoutMs = 2000;
}
