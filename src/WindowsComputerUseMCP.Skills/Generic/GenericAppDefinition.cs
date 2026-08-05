using WindowsComputerUseMCP.Core.Abstractions;
using WindowsComputerUseMCP.Core.Models;

namespace WindowsComputerUseMCP.Skills.Generic;

/// <summary>
/// 1つの汎用UIAスキルパックがどのアプリを対象とするかの定義。
/// Adobe各製品・Clipchamp等、UIA/物理操作以外の専用APIブリッジを持たないアプリはこれで表現する。
/// </summary>
public sealed record GenericAppDefinition
{
    public required string AppId { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> ProcessNames { get; init; }
}
