namespace WindowsComputerUseMCP.Safety;

/// <summary>
/// SECURITY.md に定義された「承認対象」危険操作カテゴリの検出用キーワード辞書。
/// 完全ではないため、Safety層のテストで誤検知・見逃しの傾向を継続的に把握する前提。
/// </summary>
public static class DangerousActionKeywords
{
    public const string CategoryDelete = "Delete";
    public const string CategorySend = "Send";
    public const string CategoryPurchase = "Purchase";
    public const string CategoryPay = "Pay";
    public const string CategoryPublish = "Publish";
    public const string CategoryOverwriteSave = "OverwriteSave";

    /// <summary>カテゴリ名 → 検出キーワード（日本語/英語）のマップ。</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> KeywordsByCategory =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [CategoryDelete] = ["削除", "ゴミ箱", "delete", "remove", "erase", "trash"],
            [CategorySend] = ["送信", "送る", "send", "submit", "post"],
            [CategoryPurchase] = ["購入", "buy", "purchase", "order", "checkout"],
            [CategoryPay] = ["支払い", "決済", "pay", "payment", "checkout"],
            [CategoryPublish] = ["公開", "発行", "publish", "release"],
            [CategoryOverwriteSave] = ["上書き保存", "save", "overwrite", "上書き"],
        };

    /// <summary>
    /// <paramref name="texts"/> のいずれかに危険操作カテゴリのキーワードが含まれていれば、
    /// そのカテゴリ名を返す（大文字小文字を区別しない部分一致）。該当がなければ null。
    /// </summary>
    public static string? DetectCategory(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var (category, keywords) in KeywordsByCategory)
            {
                foreach (var keyword in keywords)
                {
                    if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return category;
                    }
                }
            }
        }

        return null;
    }
}
