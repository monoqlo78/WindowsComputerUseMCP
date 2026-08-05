using System.Drawing;
using System.Drawing.Imaging;
using WindowsComputerUseMCP.Core.Models;
using WindowsComputerUseMCP.Windows.Native;

namespace WindowsComputerUseMCP.Windows.Services;

/// <summary>
/// 画面/ウィンドウ領域を <see cref="Bitmap"/> としてキャプチャする共通ロジック。
/// <see cref="ScreenCaptureService"/> と <see cref="ScreenChangeService"/> の両方から利用される。
/// </summary>
internal static class ScreenCaptureCore
{
    /// <summary>
    /// ウィンドウハンドル・モニターインデックス・仮想スクリーン全体のいずれかをキャプチャし、
    /// <paramref name="region"/> が指定されている場合はキャプチャ後の画像からその領域を切り出す。
    /// </summary>
    public static Bitmap Capture(long? windowHandle, int? monitorIndex, ScreenRect? region = null)
    {
        using Bitmap source = windowHandle is { } handle
            ? CaptureWindow((nint)handle)
            : CaptureScreenRegion(monitorIndex);

        if (region is not { } r)
        {
            return (Bitmap)source.Clone();
        }

        var cropX = Math.Max(0, Math.Min(r.X, source.Width - 1));
        var cropY = Math.Max(0, Math.Min(r.Y, source.Height - 1));
        var cropWidth = Math.Max(1, Math.Min(r.Width, source.Width - cropX));
        var cropHeight = Math.Max(1, Math.Min(r.Height, source.Height - cropY));

        return source.Clone(new Rectangle(cropX, cropY, cropWidth, cropHeight), source.PixelFormat);
    }

    public static Bitmap CaptureScreenRegion(int? monitorIndex)
    {
        ScreenRect region;
        if (monitorIndex is { } index)
        {
            var monitors = MonitorEnumerator.GetMonitors();
            region = index >= 0 && index < monitors.Count ? monitors[index].Bounds : MonitorEnumerator.GetVirtualScreenBounds();
        }
        else
        {
            region = MonitorEnumerator.GetVirtualScreenBounds();
        }

        var bitmap = new Bitmap(Math.Max(region.Width, 1), Math.Max(region.Height, 1), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.X, region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static Bitmap CaptureWindow(nint hWnd)
    {
        NativeMethods.GetWindowRect(hWnd, out var rect);
        var width = Math.Max(rect.Right - rect.Left, 1);
        var height = Math.Max(rect.Bottom - rect.Top, 1);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            var printed = NativeMethods.PrintWindow(hWnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            if (!printed)
            {
                graphics.ReleaseHdc(hdc);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return bitmap;
    }

    /// <summary>
    /// 2枚のビットマップ（同一サイズであることを前提）を比較し、差分ピクセル比率（0.0〜1.0）を返す。
    /// サイズが異なる場合は「変化あり」とみなし 1.0 を返す。
    /// </summary>
    public static double ComputeDifferenceRatio(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return 1.0;
        }

        var rect = new Rectangle(0, 0, a.Width, a.Height);
        var dataA = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dataB = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var byteCount = Math.Abs(dataA.Stride) * a.Height;
            var bufferA = new byte[byteCount];
            var bufferB = new byte[byteCount];
            System.Runtime.InteropServices.Marshal.Copy(dataA.Scan0, bufferA, 0, byteCount);
            System.Runtime.InteropServices.Marshal.Copy(dataB.Scan0, bufferB, 0, byteCount);

            const int bytesPerPixel = 4;
            var totalPixels = a.Width * a.Height;
            if (totalPixels == 0)
            {
                return 0.0;
            }

            long differingPixels = 0;
            for (var y = 0; y < a.Height; y++)
            {
                var rowOffset = y * dataA.Stride;
                for (var x = 0; x < a.Width; x++)
                {
                    var offset = rowOffset + (x * bytesPerPixel);
                    if (bufferA[offset] != bufferB[offset]
                        || bufferA[offset + 1] != bufferB[offset + 1]
                        || bufferA[offset + 2] != bufferB[offset + 2]
                        || bufferA[offset + 3] != bufferB[offset + 3])
                    {
                        differingPixels++;
                    }
                }
            }

            return (double)differingPixels / totalPixels;
        }
        finally
        {
            a.UnlockBits(dataA);
            b.UnlockBits(dataB);
        }
    }
}
