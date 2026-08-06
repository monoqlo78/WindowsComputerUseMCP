using System.Text.Json;

namespace WindowsComputerUseMCP.Skills.Abstractions;

/// <summary>
/// 各スキルパック（Blender/Figma/Illustrator等）が外部プロセス・COM・プラグインから受け取る
/// JsonElement を、呼び出し側でそのままJSONシリアライズし直せる素朴なオブジェクトグラフ
/// （Dictionary/List/プリミティブ）に変換する共通ヘルパー。
/// </summary>
public static class SkillJson
{
    public static object? ToPlainObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToPlainObject(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToPlainObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };

    /// <summary>JSON文字列をパースして素朴なオブジェクトグラフに変換する。パースに失敗した場合は元の文字列をそのまま返す。</summary>
    public static object? ParseOrRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return ToPlainObject(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
