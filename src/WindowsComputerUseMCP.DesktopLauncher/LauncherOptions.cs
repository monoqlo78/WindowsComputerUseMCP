using System.Text;

namespace WindowsComputerUseMCP.DesktopLauncher;

/// <summary>コマンドライン引数の解析結果。</summary>
internal sealed class LauncherOptions
{
    /// <summary>既定のデスクトップ名。ブラウザ自動化用に共有する。</summary>
    public const string DefaultDesktopName = "WCU_Background";

    public required string DesktopName { get; init; }

    public required string CommandLine { get; init; }

    public string? WorkingDirectory { get; init; }

    public static string Usage =>
        """
        使い方:
          wcu-desktop-launcher [--desktop <名前>] [--cwd <ディレクトリ>] -- <コマンド> [引数...]

        オプション:
          --desktop <名前>   起動先のバックグラウンドデスクトップ名。
                             英数字・ハイフン・アンダースコアのみ。既定は WCU_Background。
          --cwd <ディレクトリ> 子プロセスの作業ディレクトリ。既定は現在のディレクトリ。
          --                 これ以降をすべて実行するコマンドとして扱う。

        例:
          wcu-desktop-launcher --desktop WCU_Background -- npx -y @playwright/mcp@latest --browser msedge
        """;

    public static LauncherOptions? Parse(string[] args)
    {
        var desktop = DefaultDesktopName;
        string? cwd = null;
        var commandArgs = new List<string>();
        var separatorSeen = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (separatorSeen)
            {
                commandArgs.Add(arg);
                continue;
            }

            switch (arg)
            {
                case "--":
                    separatorSeen = true;
                    break;

                case "--desktop":
                    if (++i >= args.Length) { return null; }
                    desktop = args[i];
                    break;

                case "--cwd":
                    if (++i >= args.Length) { return null; }
                    cwd = args[i];
                    break;

                case "--help":
                case "-h":
                    return null;

                default:
                    // "--" 省略時は最初の非オプション引数以降をコマンドとみなす。
                    separatorSeen = true;
                    commandArgs.Add(arg);
                    break;
            }
        }

        if (commandArgs.Count == 0 || !IsValidDesktopName(desktop))
        {
            return null;
        }

        return new LauncherOptions
        {
            DesktopName = desktop,
            CommandLine = BuildCommandLine(commandArgs),
            WorkingDirectory = cwd,
        };
    }

    /// <summary>
    /// デスクトップ名にパス区切りや予約文字が入ると CreateDesktopW が失敗するため、
    /// 安全な文字だけに制限する。
    /// </summary>
    private static bool IsValidDesktopName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 64 &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    /// <summary>
    /// CreateProcessW に渡す単一のコマンドライン文字列を、Windows のクォート規則に従って組み立てる。
    /// </summary>
    private static string BuildCommandLine(IReadOnlyList<string> parts)
    {
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteArgument(part));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 空白や引用符を含む引数を Windows のコマンドライン規則でエスケープする。
    /// バックスラッシュは引用符の直前でのみ二重化が必要という仕様に従う。
    /// </summary>
    private static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => c is ' ' or '\t' or '\n' or '\v' or '"'))
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');

        for (var i = 0; i < argument.Length; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            builder.Append(argument[i]);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
