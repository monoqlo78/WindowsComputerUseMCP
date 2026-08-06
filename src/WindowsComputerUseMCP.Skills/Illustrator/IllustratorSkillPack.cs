using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills.Illustrator;

/// <summary>
/// Adobe Illustrator用スキルパック。
///
/// Illustratorは長年、外部プロセスからのCOM自動化（ProgID "Illustrator.Application"）と、
/// その上で動く ExtendScript（JavaScript）実行API（Application.DoJavaScript）を公式に提供している。
/// 本スキルパックはこれを直接呼び出す「公式APIブリッジ」であり、Blenderと同様、画面クリックによる
/// フォールバックは行わない（Illustrator自体のウィンドウはUI Automationからも操作可能だが、
/// ドキュメント内部のパス編集・色指定・書き出し等はスクリプトAPI経由の方が確実・高速）。
///
/// 前提: Windowsに Adobe Illustrator がインストールされ、少なくとも1回起動してCOM登録済みであること。
/// 実行時にIllustratorが起動していない場合は、その旨のエラーを返す（自動起動はしない）。
/// </summary>
public sealed class IllustratorSkillPack : ISkillPack
{
    private const string ProgId = "Illustrator.Application";

    private readonly AdobeComScriptBridge _bridge;

    public IllustratorSkillPack(ILogger<IllustratorSkillPack> logger)
    {
        _bridge = new AdobeComScriptBridge(ProgId, logger);
    }

    public string AppId => "illustrator";

    public string DisplayName => "Adobe Illustrator";

    public IReadOnlyList<string> ProcessNames => ["Illustrator"];

    public IReadOnlyList<SkillActionDescriptor> ListActions() =>
    [
        new SkillActionDescriptor
        {
            Name = "is_running",
            Description = "Illustratorが起動しておりCOM経由で操作可能かを確認する。",
        },
        new SkillActionDescriptor
        {
            Name = "document_info",
            Description = "アクティブなドキュメントの情報（ファイル名・アートボード一覧・レイヤー一覧・選択数）を取得する。",
        },
        new SkillActionDescriptor
        {
            Name = "execute_script",
            Description = "任意のExtendScript（JavaScript）をIllustrator上で実行する（強力だが危険。信頼できるコードのみ実行すること）。戻り値はJSONとしてパース可能ならパースして返す。",
            Parameters = [new SkillParameterDescriptor { Name = "script", Type = "string", Required = true, Description = "実行するExtendScript（JavaScript）コード" }],
        },
        new SkillActionDescriptor
        {
            Name = "create_rectangle",
            Description = "アクティブドキュメントに矩形パスを作成する。座標はIllustratorのドキュメント座標系（PathItems.rectangle(top, left, width, height)と同じ）。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "top", Type = "number", Required = true, Description = "上端のY座標" },
                new SkillParameterDescriptor { Name = "left", Type = "number", Required = true, Description = "左端のX座標" },
                new SkillParameterDescriptor { Name = "width", Type = "number", Required = true, Description = "幅（ポイント）" },
                new SkillParameterDescriptor { Name = "height", Type = "number", Required = true, Description = "高さ（ポイント）" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "塗り色（例: #FF0000）。省略時は塗りなし。" },
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "作成したパスアイテムの名前" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "create_ellipse",
            Description = "アクティブドキュメントに楕円パスを作成する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "top", Type = "number", Required = true, Description = "上端のY座標" },
                new SkillParameterDescriptor { Name = "left", Type = "number", Required = true, Description = "左端のX座標" },
                new SkillParameterDescriptor { Name = "width", Type = "number", Required = true, Description = "幅（ポイント）" },
                new SkillParameterDescriptor { Name = "height", Type = "number", Required = true, Description = "高さ（ポイント）" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "塗り色（例: #00FF00）。省略時は塗りなし。" },
                new SkillParameterDescriptor { Name = "name", Type = "string", Required = false, Description = "作成したパスアイテムの名前" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "create_text_frame",
            Description = "アクティブドキュメントにポイント文字（テキストフレーム）を作成する。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "top", Type = "number", Required = true, Description = "配置位置のY座標" },
                new SkillParameterDescriptor { Name = "left", Type = "number", Required = true, Description = "配置位置のX座標" },
                new SkillParameterDescriptor { Name = "text", Type = "string", Required = true, Description = "表示するテキスト" },
                new SkillParameterDescriptor { Name = "fontSize", Type = "number", Required = false, Description = "フォントサイズ（pt）。既定12。" },
                new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = false, Description = "文字色（例: #000000）。" },
            ],
        },
        new SkillActionDescriptor
        {
            Name = "set_fill_color",
            Description = "現在選択中のパスアイテムすべてに塗り色を設定する（事前に対象を選択しておくこと）。",
            Parameters = [new SkillParameterDescriptor { Name = "fillColorHex", Type = "string", Required = true, Description = "塗り色（例: #3366FF）" }],
        },
        new SkillActionDescriptor
        {
            Name = "save_document",
            Description = "アクティブドキュメントを上書き保存する。",
        },
        new SkillActionDescriptor
        {
            Name = "open_document",
            Description = "指定パスのIllustratorドキュメントを開く。",
            Parameters = [new SkillParameterDescriptor { Name = "filePath", Type = "string", Required = true, Description = "開くファイルの絶対パス" }],
        },
        new SkillActionDescriptor
        {
            Name = "export_document",
            Description = "アクティブドキュメントを画像/PDFとして書き出す。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "filePath", Type = "string", Required = true, Description = "出力先の絶対パス" },
                new SkillParameterDescriptor { Name = "format", Type = "string", Required = false, Description = "png / jpg / pdf のいずれか。既定 png。" },
            ],
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
                "is_running" => await IsRunningAsync().ConfigureAwait(false),
                "document_info" => await RunScriptAsync(DocumentInfoScript).ConfigureAwait(false),
                "execute_script" => await RunScriptAsync(arguments.GetRequiredString("script")).ConfigureAwait(false),
                "create_rectangle" => await RunScriptAsync(BuildCreateShapeScript("rectangle", arguments)).ConfigureAwait(false),
                "create_ellipse" => await RunScriptAsync(BuildCreateShapeScript("ellipse", arguments)).ConfigureAwait(false),
                "create_text_frame" => await RunScriptAsync(BuildCreateTextScript(arguments)).ConfigureAwait(false),
                "set_fill_color" => await RunScriptAsync(BuildSetFillColorScript(arguments)).ConfigureAwait(false),
                "save_document" => await RunScriptAsync("app.activeDocument.save(); JSON.stringify({saved:true});").ConfigureAwait(false),
                "open_document" => await RunScriptAsync(BuildOpenDocumentScript(arguments)).ConfigureAwait(false),
                "export_document" => await RunScriptAsync(BuildExportScript(arguments)).ConfigureAwait(false),
                _ => SkillActionOutcome.Fail($"未知のアクションです: {actionName}"),
            };
        }
        catch (SkillArgumentException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
        catch (AdobeComBridgeException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    private async Task<SkillActionOutcome> IsRunningAsync()
    {
        var running = await _bridge.IsRunningAsync().ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { running },
            running ? "Illustratorに接続できます。" : "Illustratorが起動していません。先に起動してください。");
    }

    private async Task<SkillActionOutcome> RunScriptAsync(string script)
    {
        var raw = await _bridge.ExecuteJavaScriptAsync(script).ConfigureAwait(false);
        return SkillActionOutcome.Ok(SkillJson.ParseOrRaw(raw));
    }

    private const string DocumentInfoScript = """
        (function () {
            if (app.documents.length === 0) {
                return JSON.stringify({ hasDocument: false });
            }
            var doc = app.activeDocument;
            var layers = [];
            for (var i = 0; i < doc.layers.length; i++) { layers.push(doc.layers[i].name); }
            var artboards = [];
            for (var j = 0; j < doc.artboards.length; j++) {
                var ab = doc.artboards[j];
                artboards.push({ name: ab.name, rect: [ab.artboardRect[0], ab.artboardRect[1], ab.artboardRect[2], ab.artboardRect[3]] });
            }
            return JSON.stringify({
                hasDocument: true,
                name: doc.name,
                fullPath: (doc.saved ? doc.fullName.fsName : null),
                layers: layers,
                artboards: artboards,
                selectionCount: doc.selection.length
            });
        })();
        """;

    private static string BuildCreateShapeScript(string shape, IReadOnlyDictionary<string, object?> arguments)
    {
        var top = arguments.GetDouble("top", 0)!.Value;
        var left = arguments.GetDouble("left", 0)!.Value;
        var width = arguments.GetDouble("width")
                    ?? throw new SkillArgumentException("必須パラメーター 'width' が指定されていません。");
        var height = arguments.GetDouble("height")
                     ?? throw new SkillArgumentException("必須パラメーター 'height' が指定されていません。");
        var fillColorHex = arguments.GetString("fillColorHex");
        var name = arguments.GetString("name");

        var creationCall = shape == "ellipse"
            ? $"doc.pathItems.ellipse({top.ToJsNumber()}, {left.ToJsNumber()}, {width.ToJsNumber()}, {height.ToJsNumber()})"
            : $"doc.pathItems.rectangle({top.ToJsNumber()}, {left.ToJsNumber()}, {width.ToJsNumber()}, {height.ToJsNumber()})";

        return $$"""
            (function () {
                var doc = app.activeDocument;
                var item = {{creationCall}};
                {{BuildFillColorAssignment("item", fillColorHex)}}
                {{(name is null ? "" : $"item.name = \"{EscapeJs(name)}\";")}}
                return JSON.stringify({ created: true, name: item.name });
            })();
            """;
    }

    private static string BuildCreateTextScript(IReadOnlyDictionary<string, object?> arguments)
    {
        var top = arguments.GetDouble("top", 0)!.Value;
        var left = arguments.GetDouble("left", 0)!.Value;
        var text = arguments.GetRequiredString("text");
        var fontSize = arguments.GetDouble("fontSize", 12)!.Value;
        var fillColorHex = arguments.GetString("fillColorHex");

        return $$"""
            (function () {
                var doc = app.activeDocument;
                var frame = doc.textFrames.add();
                frame.contents = "{{EscapeJs(text)}}";
                frame.top = {{top.ToJsNumber()}};
                frame.left = {{left.ToJsNumber()}};
                frame.textRange.characterAttributes.size = {{fontSize.ToJsNumber()}};
                {{BuildFillColorAssignment("frame.textRange.characterAttributes", fillColorHex, propertyName: "fillColor")}}
                return JSON.stringify({ created: true, name: frame.name });
            })();
            """;
    }

    private static string BuildSetFillColorScript(IReadOnlyDictionary<string, object?> arguments)
    {
        var fillColorHex = arguments.GetRequiredString("fillColorHex");
        var (r, g, b) = ParseHexColor(fillColorHex);

        return $$"""
            (function () {
                var doc = app.activeDocument;
                var sel = doc.selection;
                if (!sel || sel.length === 0) {
                    return JSON.stringify({ updated: 0, message: "選択中のオブジェクトがありません。" });
                }
                var color = new RGBColor();
                color.red = {{r}};
                color.green = {{g}};
                color.blue = {{b}};
                var count = 0;
                for (var i = 0; i < sel.length; i++) {
                    try {
                        sel[i].fillColor = color;
                        sel[i].filled = true;
                        count++;
                    } catch (e) { }
                }
                return JSON.stringify({ updated: count });
            })();
            """;
    }

    private static string BuildOpenDocumentScript(IReadOnlyDictionary<string, object?> arguments)
    {
        var filePath = arguments.GetRequiredString("filePath");
        return $$"""
            (function () {
                var f = new File("{{EscapeJs(filePath)}}");
                var doc = app.open(f);
                return JSON.stringify({ opened: true, name: doc.name });
            })();
            """;
    }

    private static string BuildExportScript(IReadOnlyDictionary<string, object?> arguments)
    {
        var filePath = arguments.GetRequiredString("filePath");
        var format = (arguments.GetString("format", "png") ?? "png").ToLowerInvariant();

        var exportBody = format switch
        {
            "jpg" or "jpeg" => """
                var opts = new ExportOptionsJPEG();
                opts.qualitySetting = 100;
                doc.exportFile(f, ExportType.JPEG, opts);
                """,
            "pdf" => """
                var opts = new PDFSaveOptions();
                doc.saveAs(f, opts);
                """,
            _ => """
                var opts = new ExportOptionsPNG24();
                opts.antiAliasing = true;
                opts.transparency = true;
                doc.exportFile(f, ExportType.PNG24, opts);
                """,
        };

        return $$"""
            (function () {
                var doc = app.activeDocument;
                var f = new File("{{EscapeJs(filePath)}}");
                {{exportBody}}
                return JSON.stringify({ exported: true, filePath: f.fsName, format: "{{format}}" });
            })();
            """;
    }

    private static string BuildFillColorAssignment(string targetExpression, string? fillColorHex, string propertyName = "fillColor")
    {
        if (string.IsNullOrEmpty(fillColorHex))
        {
            return string.Empty;
        }

        var (r, g, b) = ParseHexColor(fillColorHex);
        return $$"""
            (function () {
                var c = new RGBColor();
                c.red = {{r}};
                c.green = {{g}};
                c.blue = {{b}};
                {{targetExpression}}.{{propertyName}} = c;
                if ("filled" in {{targetExpression}}) { {{targetExpression}}.filled = true; }
            })();
            """;
    }

    private static (int R, int G, int B) ParseHexColor(string hex)
    {
        var cleaned = hex.TrimStart('#');
        if (cleaned.Length != 6 || !int.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            throw new SkillArgumentException($"色の指定が不正です（'#RRGGBB' 形式で指定してください）: {hex}");
        }

        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
}

file static class JsNumberExtensions
{
    public static string ToJsNumber(this double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
