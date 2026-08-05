using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills;

/// <summary>
/// DIに登録された全 <see cref="ISkillPack"/> を集約し、AppId で検索できるようにするレジストリ。
/// MCPツール層（SkillTools）はこれ経由でのみスキルパックにアクセスする。
/// </summary>
public sealed class SkillRegistry
{
    private readonly IReadOnlyDictionary<string, ISkillPack> _packsByAppId;

    public SkillRegistry(IEnumerable<ISkillPack> packs)
    {
        _packsByAppId = packs.ToDictionary(p => p.AppId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>登録済みの全スキルパックを返す。</summary>
    public IReadOnlyList<ISkillPack> ListPacks() => _packsByAppId.Values
        .OrderBy(p => p.AppId, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>指定した appId のスキルパックを返す。存在しない場合は null。</summary>
    public ISkillPack? Find(string appId) =>
        _packsByAppId.TryGetValue(appId, out var pack) ? pack : null;
}
