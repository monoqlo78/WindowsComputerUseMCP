namespace WindowsComputerUseMCP.Core.Models;

/// <summary>マウスボタンの種類。</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>文字列一致方法。ウィンドウタイトルやUI要素名の検索に使用する。</summary>
public enum MatchMode
{
    /// <summary>完全一致。</summary>
    Exact,

    /// <summary>部分一致（既定）。</summary>
    Contains,

    /// <summary>前方一致。</summary>
    StartsWith,

    /// <summary>正規表現一致。</summary>
    Regex,
}
