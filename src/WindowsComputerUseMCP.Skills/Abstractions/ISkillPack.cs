namespace WindowsComputerUseMCP.Skills.Abstractions;

/// <summary>
/// 「アプリスキルパック」の1つ分。特定アプリケーション（Blender, Adobe製品, Clipchamp等）を
/// 操作するための「知識（そのアプリのUI構造・API有無）」と「操作コード」を1セットにまとめたもの。
///
/// 各パックは可能な限りアプリ公式のAPI/スクリプト連携（例: BlenderMCPのTCPブリッジ）を優先し、
/// APIが存在しない・不足している操作についてのみ、WindowsComputerUseMCPの汎用画面操作
/// （スクリーンショット + UI Automation + マウス/キーボード入力）にフォールバックする。
/// </summary>
public interface ISkillPack
{
    /// <summary>スキルパックを一意に識別するID（例: "blender", "clipchamp", "photoshop"）。小文字推奨。</summary>
    string AppId { get; }

    /// <summary>人間向けの表示名（例: "Blender"）。</summary>
    string DisplayName { get; }

    /// <summary>
    /// このスキルパックが対象とするプロセス名の一覧（拡張子なし、例: "blender"）。
    /// window_list 等での自動検出・対象アプリが起動中かどうかの判定に用いる。
    /// </summary>
    IReadOnlyList<string> ProcessNames { get; }

    /// <summary>このスキルパックが提供するアクション一覧を返す（呼び出し前にクライアントが把握するため）。</summary>
    IReadOnlyList<SkillActionDescriptor> ListActions();

    /// <summary>指定したアクションを実行する。</summary>
    /// <param name="actionName">ListActions() が返す SkillActionDescriptor.Name のいずれか。</param>
    /// <param name="arguments">アクション固有の引数（JSON相当のプリミティブ値・配列・辞書）。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    Task<SkillActionOutcome> InvokeAsync(
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>スキルパックが提供する1アクションの説明（一覧表示・自己文書化用）。</summary>
public sealed record SkillActionDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<SkillParameterDescriptor> Parameters { get; init; } = [];
}

/// <summary>アクション1パラメーターの説明。</summary>
public sealed record SkillParameterDescriptor
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Required { get; init; }
    public string? Description { get; init; }
}

/// <summary>アクション実行結果。</summary>
public sealed record SkillActionOutcome
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>アクション固有の戻り値（JSON化して返却する）。</summary>
    public object? Data { get; init; }

    public static SkillActionOutcome Ok(object? data = null, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static SkillActionOutcome Fail(string message, object? data = null) =>
        new() { Success = false, Message = message, Data = data };
}
