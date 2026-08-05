namespace WindowsComputerUseMCP.Windows.Native;

/// <summary>
/// <c>keyboard_press</c>/<c>keyboard_hotkey</c> で受け取るキー名文字列を
/// 仮想キーコード (VK_*) へ変換するヘルパー。
/// </summary>
internal static class VirtualKeyMap
{
    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Tab"] = 0x09,
        ["Escape"] = 0x1B,
        ["Esc"] = 0x1B,
        ["Space"] = 0x20,
        ["Backspace"] = 0x08,
        ["Delete"] = 0x2E,
        ["Del"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Up"] = 0x26,
        ["Down"] = 0x28,
        ["Left"] = 0x25,
        ["Right"] = 0x27,
        ["Ctrl"] = 0x11,
        ["Control"] = 0x11,
        ["Alt"] = 0x12,
        ["Shift"] = 0x10,
        ["Win"] = 0x5B,
        ["Windows"] = 0x5B,
        ["CapsLock"] = 0x14,
        ["NumLock"] = 0x90,
        ["PrintScreen"] = 0x2C,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
    };

    /// <summary>
    /// キー名（例: "Enter", "A", "F5", "Ctrl"）を仮想キーコードへ変換する。
    /// 未知のキー名の場合は null を返す。
    /// </summary>
    public static ushort? TryResolve(string keyName)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            return null;
        }

        if (NamedKeys.TryGetValue(keyName, out var namedCode))
        {
            return namedCode;
        }

        if (keyName.Length == 1)
        {
            var c = char.ToUpperInvariant(keyName[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return (ushort)c;
            }

            if (c is >= '0' and <= '9')
            {
                return (ushort)c;
            }
        }

        return null;
    }
}
