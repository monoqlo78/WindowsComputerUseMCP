using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WindowsComputerUseMCP.Skills.Illustrator;

/// <summary>
/// Adobeアプリケーション（Illustrator, Photoshop等）が長年提供している「外部COM自動化オブジェクト
/// （ProgID: "Illustrator.Application" 等）+ Application.DoJavaScript(ExtendScript)」による
/// スクリプト連携ブリッジ。BlenderMCPアドオンのTCPソケットと同様、公式のスクリプト実行APIを
/// 直接呼び出す「公式APIブリッジ」であり、画面クリックによる操作は一切行わない。
///
/// COM相互運用は取得したオブジェクトのアパートメント境界を安定させるため、専用のSTAスレッド上で
/// すべての呼び出しを実行する（一般的なOffice/Adobe COM自動化のベストプラクティス）。
/// </summary>
public sealed class AdobeComScriptBridge : IDisposable
{
    private readonly string _progId;
    private readonly ILogger _logger;
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly Thread _staThread;

    public AdobeComScriptBridge(string progId, ILogger logger)
    {
        _progId = progId;
        _logger = logger;
        _staThread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = $"AdobeComBridge-{progId}",
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    /// <summary>対象アプリケーションが起動しており、COMオブジェクトを取得できるかを確認する。</summary>
    public Task<bool> IsRunningAsync() => RunOnStaAsync(() =>
    {
        try
        {
            var obj = GetActiveObjectByProgId(_progId);
            if (obj is not null)
            {
                Marshal.ReleaseComObject(obj);
            }

            return true;
        }
        catch (COMException)
        {
            return false;
        }
    });

    /// <summary>
    /// 起動中のアプリケーションに対して ExtendScript（JavaScript）を実行し、結果を文字列で返す。
    /// アプリケーションが起動していない場合は <see cref="AdobeComBridgeException"/> をスローする。
    /// </summary>
    public Task<string> ExecuteJavaScriptAsync(string script) => RunOnStaAsync<string>(() =>
    {
        dynamic app;
        try
        {
            app = GetActiveObjectByProgId(_progId);
        }
        catch (COMException ex)
        {
            throw new AdobeComBridgeException(
                $"{_progId} が起動していません。対象アプリケーションを先に起動してから再実行してください。", ex);
        }

        try
        {
            string result = app.DoJavaScript(script, null);
            return result ?? string.Empty;
        }
        catch (COMException ex)
        {
            throw new AdobeComBridgeException($"ExtendScriptの実行中にエラーが発生しました: {ex.Message}", ex);
        }
    });

    /// <summary>
    /// .NET Core/.NET 5+ では <c>Marshal.GetActiveObject</c> が廃止されているため、実行中オブジェクトテーブル
    /// （ROT）から ProgID 経由でアクティブなCOMオブジェクトを取得する処理を、必要なOLE API呼び出しで再実装する。
    /// </summary>
    private static object GetActiveObjectByProgId(string progId)
    {
        var hr = CLSIDFromProgID(progId, out var clsid);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
        return obj;
    }

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    private Task<T> RunOnStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    private void RunLoop()
    {
        foreach (var action in _workQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdobeComScriptBridge({ProgId}) の作業スレッドで予期しない例外が発生しました。", _progId);
            }
        }
    }

    public void Dispose()
    {
        _workQueue.CompleteAdding();
    }
}

/// <summary>Adobeアプリケーションとのスクリプト連携に失敗した場合の例外。</summary>
public sealed class AdobeComBridgeException : Exception
{
    public AdobeComBridgeException(string message) : base(message)
    {
    }

    public AdobeComBridgeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
