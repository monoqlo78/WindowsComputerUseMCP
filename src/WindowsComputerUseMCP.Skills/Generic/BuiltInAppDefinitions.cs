namespace WindowsComputerUseMCP.Skills.Generic;

/// <summary>
/// 既定で登録する汎用スキルパック（Adobe各製品・Clipchamp等）の対象アプリ定義一覧。
/// 新しいアプリを追加する場合は、専用スキルパックが無ければまずここに1行足すだけで
/// find_window/click_element/screenshot 等の基本操作が使えるようになる。
/// </summary>
public static class BuiltInAppDefinitions
{
    public static IReadOnlyList<GenericAppDefinition> All { get; } =
    [
        new GenericAppDefinition
        {
            AppId = "clipchamp",
            DisplayName = "Clipchamp",
            ProcessNames = ["Clipchamp", "MicrosoftClipchamp"],
        },
        new GenericAppDefinition
        {
            AppId = "photoshop",
            DisplayName = "Adobe Photoshop",
            ProcessNames = ["Photoshop"],
        },
        new GenericAppDefinition
        {
            AppId = "premiere",
            DisplayName = "Adobe Premiere Pro",
            ProcessNames = ["Adobe Premiere Pro", "AdobePremierePro"],
        },
        new GenericAppDefinition
        {
            AppId = "aftereffects",
            DisplayName = "Adobe After Effects",
            ProcessNames = ["AfterFX"],
        },
        new GenericAppDefinition
        {
            AppId = "indesign",
            DisplayName = "Adobe InDesign",
            ProcessNames = ["InDesign"],
        },
        new GenericAppDefinition
        {
            AppId = "lightroom",
            DisplayName = "Adobe Lightroom Classic",
            ProcessNames = ["Lightroom"],
        },
        new GenericAppDefinition
        {
            AppId = "audition",
            DisplayName = "Adobe Audition",
            ProcessNames = ["Adobe Audition"],
        },
        new GenericAppDefinition
        {
            AppId = "davinciresolve",
            DisplayName = "DaVinci Resolve",
            ProcessNames = ["Resolve"],
        },
        new GenericAppDefinition
        {
            AppId = "capcut",
            DisplayName = "CapCut",
            ProcessNames = ["CapCut"],
        },
        new GenericAppDefinition
        {
            AppId = "gimp",
            DisplayName = "GIMP",
            ProcessNames = ["gimp", "gimp-2.10"],
        },
        new GenericAppDefinition
        {
            AppId = "krita",
            DisplayName = "Krita",
            ProcessNames = ["krita"],
        },
    ];
}
