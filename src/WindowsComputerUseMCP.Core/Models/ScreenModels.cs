namespace WindowsComputerUseMCP.Core.Models;

/// <summary>仮想スクリーン座標系での整数座標点。</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>仮想スクリーン座標系での矩形領域。</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(ScreenPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public static readonly ScreenRect Empty = new(0, 0, 0, 0);
}

/// <summary>モニター（ディスプレイ）1台分の情報。</summary>
public sealed record MonitorInfo
{
    public required int Index { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required ScreenRect WorkArea { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>このモニターのDPIスケール（100% = 1.0）。</summary>
    public required double DpiScale { get; init; }
}
