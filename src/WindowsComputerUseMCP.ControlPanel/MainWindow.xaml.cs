using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsComputerUseMCP.ControlPanel.Services;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.ControlPanel;

/// <summary>
/// Interaction logic for MainWindow.xaml
///
/// Server (MCP stdio プロセス) の緊急停止状態を名前付きパイプ経由で監視・操作し、
/// 監査ログ（%LOCALAPPDATA%\WindowsComputerUseMCP\Logs）を一覧表示する簡易監視/操作 UI。
/// </summary>
public partial class MainWindow : Window
{
    private readonly ServerIpcClient _ipcClient = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly ObservableCollection<AuditLogRow> _logRows = [];
    private bool _isBusy;
    private bool _isPolling;
    private int _stateVersion;

    public MainWindow()
    {
        InitializeComponent();

        AuditLogGrid.ItemsSource = _logRows;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();

        RefreshLogs();
        _ = PollAsync();
    }

    private async void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _stateVersion++;
        try
        {
            var response = await _ipcClient.SendAsync(IpcCommand.Activate, "ControlPanel の「緊急停止」ボタン");
            ApplyResponse(response);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _stateVersion++;
        try
        {
            var response = await _ipcClient.SendAsync(IpcCommand.Deactivate);
            ApplyResponse(response);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await PollAsync();
        RefreshLogs();
    }

    private async Task PollAsync()
    {
        if (_isBusy || _isPolling)
        {
            return;
        }

        _isPolling = true;
        var version = _stateVersion;
        try
        {
            var response = await _ipcClient.SendAsync(IpcCommand.Status);
            if (version == _stateVersion)
            {
                ApplyResponse(response);
            }
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void ApplyResponse(IpcResponse? response)
    {
        if (response is null)
        {
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
            StatusText.Text = "サーバー未接続";
            StatusDetailText.Text = "MCPクライアントから WindowsComputerUseMCP.Server を起動すると接続されます。";
            FooterText.Text = $"最終更新: {DateTime.Now:HH:mm:ss}（未接続）";
            return;
        }

        if (!response.Success)
        {
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0x00));
            StatusText.Text = "状態未確認（一部または全サーバーで失敗）";
            StatusDetailText.Text = response.Message ?? string.Empty;
        }
        else if (response.EmergencyStopActive)
        {
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
            StatusText.Text = "緊急停止中（一部または全サーバー）";
            StatusDetailText.Text = "停止状態のサーバーは入力を拒否します。「緊急停止」で全接続先を停止し、「解除」で復帰します。";
        }
        else
        {
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C));
            StatusText.Text = "監視中（正常）";
            StatusDetailText.Text = response.Message ?? string.Empty;
        }

        FooterText.Text = $"最終更新: {DateTime.Now:HH:mm:ss}";
    }

    private void RefreshLogs()
    {
        _logRows.Clear();
        foreach (var entry in AuditLogReader.ReadRecent())
        {
            _logRows.Add(new AuditLogRow(
                entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                entry.ToolName,
                entry.TargetWindow ?? "-",
                entry.TargetProcess ?? "-",
                entry.Result,
                Math.Round(entry.DurationMs, 1)));
        }
    }

    private sealed record AuditLogRow(
        string TimestampLocal,
        string ToolName,
        string TargetWindow,
        string TargetProcess,
        string Result,
        double DurationMs);
}
