using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills.Figma;

/// <summary>
/// Figma（デスクトップ版）用スキルパック。
///
/// FigmaはBlenderのようなTCPサーバーやAdobeのようなCOM自動化オブジェクトを提供していないため、
/// tools/figma-plugin に用意した専用プラグインをFigma側で実行してもらい、そのプラグインが
/// <see cref="FigmaBridgeServer"/> のWebSocketサーバーへ接続してくる方式で連携する。
/// プラグインは figma.* Plugin API を直接呼び出すため、キャンバス上のノード作成・色設定・
/// エクスポート等をUI操作なしに正確に行える。
/// </summary>
public sealed class FigmaSkillPack(FigmaBridgeServer bridge, ILogger<FigmaSkillPack> logger) : ISkillPack
{
    public string AppId => "figma";

    public string DisplayName => "Figma (デスクトップ版)";

    public IReadOnlyList<string> ProcessNames => ["Figma"];

    public IReadOnlyList<SkillActionDescriptor> ListActions() =>
    [
        new SkillActionDescriptor
        {
            Name = "is_connected",
            Description = "Figma連携プラグインが接続済みかを確認する。",
        },
        new SkillActionDescriptor
        {
            Name = "document_info",
            Description = "現在開いているFigmaファイルの情報（ページ一覧・現在の選択）を取得する。",
        },
        new SkillActionDescriptor
        {
            Name = "get_selection",
            Description = "現在のキャンバス上での選択ノード一覧（id・種類・名前・位置・サイズ）を取得する。",
        },
        new SkillActionDescriptor
        {
            Name = "create_rectangle",
            Description = "現在のページに矩形ノードを作成する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "x", Type = "number", Required = true, Description = "X座標" },
                new SkillParameterDescriptor { Name = "y", Type = "number", Required = true, Description = "Y座標" },
                new SkillParameterDescriptor { Name = "width", Type = "number", Required = true, Description = "幅" },
                new SkillParameterDescriptor { Name = "height", Type = "number", Required = true, Description = "高さ" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "塗り色（例: #FF0000）" },
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "ノード名" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "create_ellipse",
            Description = "現在のページに楕円ノードを作成する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "x", Type = "number", Required = true, Description = "X座標" },
                new SkillParameterDescriptor { Name = "y", Type = "number", Required = true, Description = "Y座標" },
                new SkillParameterDescriptor { Name = "width", Type = "number", Required = true, Description = "幅" },
                new SkillParameterDescriptor { Name = "height", Type = "number", Required = true, Description = "高さ" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "塗り色（例: #00FF00）" },
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "ノード名" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "create_text",
            Description = "現在のページにテキストノードを作成する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "x", Type = "number", Required = true, Description = "X座標" },
                new SkillParameterDescriptor { Name = "y", Type = "number", Required = true, Description = "Y座標" },
                new SkillParameterDescriptor { Name = "characters", Type = "string", Required = true, Description = "表示するテキスト" },
                new SkillParameterDescriptor { Name = "fontSize", Type = "number", Required = false, Description = "フォントサイズ。既定16。" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "文字色（例: #000000）" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "set_fill_color",
            Description = "指定ノードの塗り色を設定する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "nodeId", Type = "string", Required = true, Description = "対象ノードのid（get_selection等で取得）" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = true, Description = "塗り色（例: #3366FF）" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "delete_node",
            Description = "指定ノードを削除する。",
            Parameters = [new SkillParameterDescriptor { Name = "nodeId", Type = "string", Required = true, Description = "削除するノードのid" }],
        },
        new SkillActionDescriptor
        {
            Name = "export_node_image",
            Description = "指定ノード（省略時は現在の選択）を画像として書き出し、Base64データを返す。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "nodeId", Type = "string", Required = false, Description = "対象ノードのid。省略時は現在の選択。" },
                new SkillParameterDescriptor { Name = "format", Type = "string", Required = false, Description = "PNG / JPG / SVG のいずれか。既定PNG。" },
                new SkillParameterDescriptor { Name = "scale", Type = "number", Required = false, Description = "書き出し倍率。既定1。" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "list_pages",
            Description = "現在のFigmaファイルのページ一覧を取得する。",
        },
        new SkillActionDescriptor
        {
            Name = "execute_plugin_code",
            Description = "任意のFigma Plugin APIコード（JavaScript、figma.*にアクセス可能）をプラグインのサンドボックス内で実行する（強力だが危険。信頼できるコードのみ実行すること）。",
            Parameters = [new SkillParameterDescriptor { Name = "code", Type = "string", Required = true, Description = "実行するJavaScriptコード（最後の式の評価結果、またはreturnした値が結果として返る）" }],
        },
    ];

    public async Task<SkillActionOutcome> InvokeAsync(
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return actionName switch
            {
                "is_connected" => SkillActionOutcome.Ok(new { connected = bridge.IsPluginConnected },
                    bridge.IsPluginConnected
                        ? "Figma連携プラグインに接続できます。"
                        : "Figma連携プラグインが接続されていません。tools/figma-plugin を開発用プラグインとしてFigmaで実行してください。"),
                "document_info" => await ForwardAsync("get_document_info", null, cancellationToken).ConfigureAwait(false),
                "get_selection" => await ForwardAsync("get_selection", null, cancellationToken).ConfigureAwait(false),
                "create_rectangle" => await ForwardAsync("create_rectangle", BuildShapeParams(arguments), cancellationToken).ConfigureAwait(false),
                "create_ellipse" => await ForwardAsync("create_ellipse", BuildShapeParams(arguments), cancellationToken).ConfigureAwait(false),
                "create_text" => await ForwardAsync("create_text", new
                {
                    x = arguments.GetDouble("x", 0),
                    y = arguments.GetDouble("y", 0),
                    characters = arguments.GetRequiredString("characters"),
                    fontSize = arguments.GetDouble("fontSize", 16),
                    fillColorHex = arguments.GetString("fillColorHex"),
                }, cancellationToken).ConfigureAwait(false),
                "set_fill_color" => await ForwardAsync("set_fill_color", new
                {
                    nodeId = arguments.GetRequiredString("nodeId"),
                    fillColorHex = arguments.GetRequiredString("fillColorHex"),
                }, cancellationToken).ConfigureAwait(false),
                "delete_node" => await ForwardAsync("delete_node", new { nodeId = arguments.GetRequiredString("nodeId") }, cancellationToken).ConfigureAwait(false),
                "export_node_image" => await ForwardAsync("export_node_image", new
                {
                    nodeId = arguments.GetString("nodeId"),
                    format = (arguments.GetString("format", "PNG") ?? "PNG").ToUpperInvariant(),
                    scale = arguments.GetDouble("scale", 1),
                }, cancellationToken).ConfigureAwait(false),
                "list_pages" => await ForwardAsync("list_pages", null, cancellationToken).ConfigureAwait(false),
                "execute_plugin_code" => await ForwardAsync("execute_code", new { code = arguments.GetRequiredString("code") }, cancellationToken).ConfigureAwait(false),
                _ => SkillActionOutcome.Fail($"未知のアクションです: {actionName}"),
            };
        }
        catch (SkillArgumentException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    private static object BuildShapeParams(IReadOnlyDictionary<string, object?> arguments) => new
    {
        x = arguments.GetDouble("x", 0),
        y = arguments.GetDouble("y", 0),
        width = arguments.GetDouble("width") ?? throw new SkillArgumentException("必須パラメーター 'width' が指定されていません。"),
        height = arguments.GetDouble("height") ?? throw new SkillArgumentException("必須パラメーター 'height' が指定されていません。"),
        fillColorHex = arguments.GetString("fillColorHex"),
        name = arguments.GetString("name"),
    };

    private async Task<SkillActionOutcome> ForwardAsync(string commandType, object? parameters, CancellationToken cancellationToken)
    {
        try
        {
            var result = await bridge.SendCommandAsync(commandType, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            return SkillActionOutcome.Ok(SkillJson.ToPlainObject(result));
        }
        catch (FigmaBridgeException ex)
        {
            logger.LogWarning("Figmaコマンド {CommandType} が失敗しました: {Message}", commandType, ex.Message);
            return SkillActionOutcome.Fail(ex.Message);
        }
    }
}
