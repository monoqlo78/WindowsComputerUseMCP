using System.Text.Json;

namespace WindowsComputerUseMCP.Skills.Abstractions;

/// <summary>
/// SkillActionOutcome の Arguments 辞書（object? 値、実体はプリミティブ・JsonElement・配列等）から
/// 型安全に値を取り出すためのヘルパー。MCPツール層からはJSON文字列として渡ってくるため、
/// 各スキルパックの実装で毎回同じ変換処理を書かずに済むようにする。
/// </summary>
public static class SkillArguments
{
    public static string? GetString(this IReadOnlyDictionary<string, object?> args, string name, string? defaultValue = null)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => defaultValue,
            _ => value.ToString(),
        };
    }

    public static string GetRequiredString(this IReadOnlyDictionary<string, object?> args, string name)
    {
        var value = GetString(args, name);
        if (string.IsNullOrEmpty(value))
        {
            throw new SkillArgumentException($"必須パラメーター '{name}' が指定されていません。");
        }

        return value;
    }

    public static int? GetInt(this IReadOnlyDictionary<string, object?> args, string name, int? defaultValue = null)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static double? GetDouble(this IReadOnlyDictionary<string, object?> args, string name, double? defaultValue = null)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static bool GetBool(this IReadOnlyDictionary<string, object?> args, string name, bool defaultValue = false)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static IReadOnlyList<string> GetStringList(this IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
        {
            var list = new List<string>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (s is not null)
                    {
                        list.Add(s);
                    }
                }
            }

            return list;
        }

        if (value is IEnumerable<object?> enumerable)
        {
            return enumerable.Select(o => o?.ToString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }

        // 単一文字列が渡された場合も1要素のリストとして扱う（利便性のため）。
        var single = GetString(args, name);
        return single is null ? [] : [single];
    }
}

/// <summary>スキルアクションの引数が不正・不足している場合にスローする例外。</summary>
public sealed class SkillArgumentException(string message) : Exception(message);
