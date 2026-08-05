namespace WindowsComputerUseMCP.Core.Configuration;

/// <summary>ControlPanel (WPF) と Server (MCP stdio プロセス) 間の名前付きパイプ IPC に関する定数。</summary>
public static class IpcConstants
{
    /// <summary>名前付きパイプ名。ローカルマシン内の同一ユーザーセッションでのみ使用する想定。</summary>
    public const string PipeName = "WindowsComputerUseMCP.ControlPanel.v1";

    /// <summary>ControlPanel からの接続試行タイムアウト（ミリ秒）。Server が未起動の場合に速やかに諦めるための値。</summary>
    public const int ClientConnectTimeoutMs = 500;
}
