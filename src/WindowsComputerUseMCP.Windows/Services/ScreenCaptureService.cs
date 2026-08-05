using System.Drawing;
using System.Drawing.Imaging;
using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Configuration;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// GDI（<c>Graphics.CopyFromScreen</c> / <c>PrintWindow</c>）を用いた画面・ウィンドウキャプチャ。
/// 画像は PNG として扱う。
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    /// <summary>Base64往復に含める前の生データサイズの上限（超えた場合はImageBase64を省略しファイルのみ返す）。</summary>
    private const int InlineImageMaxBytes = 4 * 1024 * 1024;

    public Task<ScreenCaptureResult> CaptureAsync(ScreenCaptureRequest request, CancellationToken cancellationToken = default)
    {
        using Bitmap bitmap = request.WindowHandle is { } handle
            ? ScreenCaptureCore.CaptureWindow((nint)handle)
            : ScreenCaptureCore.CaptureScreenRegion(request.MonitorIndex);

        if (request.IncludeCursor)
        {
            DrawCursorOverlay(bitmap, request);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();

        string? filePath = null;
        if (request.SaveToFile)
        {
            UserDataPaths.EnsureDirectoriesExist();
            filePath = Path.Combine(UserDataPaths.ScreenshotsDirectory, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");
            File.WriteAllBytes(filePath, bytes);
        }

        var target = request.WindowHandle is { } h
            ? $"Window:{h}"
            : request.MonitorIndex is { } idx
                ? $"Monitor:{idx}"
                : "VirtualScreen";

        var result = new ScreenCaptureResult
        {
            Width = bitmap.Width,
            Height = bitmap.Height,
            Target = target,
            MimeType = "image/png",
            FilePath = filePath,
            ImageBase64 = bytes.Length <= InlineImageMaxBytes ? Convert.ToBase64String(bytes) : null,
        };

        return Task.FromResult(result);
    }

    private static void DrawCursorOverlay(Bitmap bitmap, ScreenCaptureRequest request)
    {
        var cursorInfo = new NativeMethods.CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref cursorInfo) || (cursorInfo.flags & NativeMethods.CURSOR_SHOWING) == 0)
        {
            return;
        }

        ScreenRect origin;
        if (request.WindowHandle is { } handle)
        {
            NativeMethods.GetWindowRect((nint)handle, out var rect);
            origin = new ScreenRect(rect.Left, rect.Top, 0, 0);
        }
        else if (request.MonitorIndex is { } index)
        {
            var monitors = MonitorEnumerator.GetMonitors();
            origin = index >= 0 && index < monitors.Count ? monitors[index].Bounds : MonitorEnumerator.GetVirtualScreenBounds();
        }
        else
        {
            origin = MonitorEnumerator.GetVirtualScreenBounds();
        }

        var x = cursorInfo.ptScreenPos.X - origin.X;
        var y = cursorInfo.ptScreenPos.Y - origin.Y;
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
        {
            return;
        }

        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            NativeMethods.DrawIcon(hdc, x, y, cursorInfo.hCursor);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }
}
