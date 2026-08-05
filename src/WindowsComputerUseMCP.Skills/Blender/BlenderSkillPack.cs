using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills.Blender;

/// <summary>
/// Blender用スキルパック。
///
/// BlenderMCPアドオン（https://github.com/ahujasid/blender-mcp 系のオープンソースアドオンを
/// Blender側にインストール・有効化しておく想定）が開くTCPソケット（既定ポート9876）に対して
/// <see cref="BlenderBridgeClient"/> 経由で直接コマンドを送る。Blenderのビューポート/メニューUIは
/// 独自GPU描画でWindows UI Automationからは見えないため、この「公式APIブリッジ経由」が
/// 唯一の確実な操作手段であり、画面クリックによる操作は行わない。
///
/// 特に generate_3d_from_image は、従来6ステップ（トライアルキー取得→有効化→ジョブ作成→
/// ポーリング→インポート→確認）に分かれていた「画像→3Dモデル生成」を1アクションに集約したもの。
/// </summary>
public sealed class BlenderSkillPack(BlenderBridgeClient bridge, ILogger<BlenderSkillPack> logger) : ISkillPack
{
    public string AppId => "blender";

    public string DisplayName => "Blender";

    public IReadOnlyList<string> ProcessNames => ["blender"];

    public IReadOnlyList<SkillActionDescriptor> ListActions() =>
    [
        new SkillActionDescriptor
        {
            Name = "is_connected",
            Description = "BlenderMCPアドオンのソケットサーバーに接続できるか確認する。",
        },
        new SkillActionDescriptor
        {
            Name = "scene_info",
            Description = "現在のBlenderシーンの情報（オブジェクト一覧・種別・座標等）を取得する。",
        },
        new SkillActionDescriptor
        {
            Name = "object_info",
            Description = "指定した名前のオブジェクトの詳細情報を取得する。",
            Parameters = [new SkillParameterDescriptor { Name = "name", Type = "string", Required = true, Description = "オブジェクト名" }],
        },
        new SkillActionDescriptor
        {
            Name = "execute_code",
            Description = "任意のBlender Pythonコードをメインスレッドで実行する（強力だが危険。信頼できるコードのみ実行すること）。",
            Parameters = [new SkillParameterDescriptor { Name = "code", Type = "string", Required = true, Description = "実行するPythonコード" }],
        },
        new SkillActionDescriptor
        {
            Name = "viewport_screenshot",
            Description = "現在の3Dビューポートのスクリーンショットを取得する（ファイル保存 + Base64データを返す）。",
            Parameters = [new SkillParameterDescriptor { Name = "maxSize", Type = "int", Required = false, Description = "最大辺のピクセル数。既定800。" }],
        },
        new SkillActionDescriptor
        {
            Name = "ensure_hyper3d_enabled",
            Description = "Hyper3D Rodin連携が無効な場合、無料トライアルAPIキーを取得して有効化する（有効化のためのUIクリックは不要。Scene設定への直接書き込みで完結する）。",
        },
        new SkillActionDescriptor
        {
            Name = "generate_3d_from_image",
            Description = "1枚以上の画像からHyper3D Rodinで3Dモデルを生成し、生成完了を待ってシーンにインポートするところまでを一括で行う。",
            Parameters =
            [
                new SkillParameterDescriptor { Name = "imagePaths", Type = "string[]", Required = true, Description = "入力画像の絶対パス（1枚以上）" },
                new SkillParameterDescriptor { Name = "textPrompt", Type = "string", Required = false, Description = "追加のテキストプロンプト（任意）" },
                new SkillParameterDescriptor { Name = "objectName", Type = "string", Required = false, Description = "インポート後のオブジェクト名。既定 GeneratedModel。" },
                new SkillParameterDescriptor { Name = "pollIntervalMs", Type = "int", Required = false, Description = "生成状況の確認間隔（ミリ秒）。既定5000。" },
                new SkillParameterDescriptor { Name = "timeoutMs", Type = "int", Required = false, Description = "生成完了を待つ最大時間（ミリ秒）。既定600000（10分）。" },
            ],
        },
    ];

    public async Task<SkillActionOutcome> InvokeAsync(
        string actionName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        return actionName switch
        {
            "is_connected" => await IsConnectedAsync(cancellationToken).ConfigureAwait(false),
            "scene_info" => await ForwardAsync("get_scene_info", null, cancellationToken).ConfigureAwait(false),
            "object_info" => await ForwardAsync("get_object_info", new { name = arguments.GetRequiredString("name") }, cancellationToken).ConfigureAwait(false),
            "execute_code" => await ForwardAsync("execute_code", new { code = arguments.GetRequiredString("code") }, cancellationToken).ConfigureAwait(false),
            "viewport_screenshot" => await ViewportScreenshotAsync(arguments, cancellationToken).ConfigureAwait(false),
            "ensure_hyper3d_enabled" => await EnsureHyper3DEnabledAsync(cancellationToken).ConfigureAwait(false),
            "generate_3d_from_image" => await GenerateFromImageAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => SkillActionOutcome.Fail($"未知のアクションです: {actionName}"),
        };
    }

    private async Task<SkillActionOutcome> IsConnectedAsync(CancellationToken cancellationToken)
    {
        var reachable = await bridge.IsReachableAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return SkillActionOutcome.Ok(new { connected = reachable },
            reachable
                ? "BlenderMCPアドオンに接続できます。"
                : "BlenderMCPアドオンに接続できません。Blenderが起動しているか、アドオンのソケットサーバーが開始されているか確認してください。");
    }

    private async Task<SkillActionOutcome> ForwardAsync(string commandType, object? parameters, CancellationToken cancellationToken)
    {
        try
        {
            var result = await bridge.SendCommandAsync(commandType, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            return SkillActionOutcome.Ok(JsonElementToPlainObject(result));
        }
        catch (BlenderBridgeException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    private async Task<SkillActionOutcome> ViewportScreenshotAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var maxSize = arguments.GetInt("maxSize", 800)!.Value;
        var tempDir = Path.Combine(Path.GetTempPath(), "WindowsComputerUseMCP", "BlenderViewport");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, $"viewport-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");

        try
        {
            await bridge.SendCommandAsync("get_viewport_screenshot",
                new { max_size = maxSize, filepath = filePath, format = "png" },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!File.Exists(filePath))
            {
                return SkillActionOutcome.Fail("Blender側でスクリーンショットの保存に失敗しました。");
            }

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            return SkillActionOutcome.Ok(new
            {
                filePath,
                base64Png = Convert.ToBase64String(bytes),
            });
        }
        catch (BlenderBridgeException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    private async Task<SkillActionOutcome> EnsureHyper3DEnabledAsync(CancellationToken cancellationToken)
    {
        const string code = """
            import bpy
            scene = bpy.context.scene
            already_enabled = scene.blendermcp_use_hyper3d
            if not already_enabled:
                if not scene.blendermcp_hyper3d_api_key:
                    bpy.ops.blendermcp.set_hyper3d_free_trial_api_key()
                scene.blendermcp_use_hyper3d = True
            print({
                "wasAlreadyEnabled": already_enabled,
                "enabled": scene.blendermcp_use_hyper3d,
                "mode": scene.blendermcp_hyper3d_mode,
            })
            """;

        try
        {
            var result = await bridge.SendCommandAsync("execute_code", new { code }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return SkillActionOutcome.Ok(JsonElementToPlainObject(result), "Hyper3D Rodin連携が有効です。");
        }
        catch (BlenderBridgeException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    private async Task<SkillActionOutcome> GenerateFromImageAsync(IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var imagePaths = arguments.GetStringList("imagePaths");
        if (imagePaths.Count == 0)
        {
            return SkillActionOutcome.Fail("imagePaths が指定されていません。1枚以上の画像パスを指定してください。");
        }

        foreach (var path in imagePaths)
        {
            if (!File.Exists(path))
            {
                return SkillActionOutcome.Fail($"画像ファイルが見つかりません: {path}");
            }
        }

        var textPrompt = arguments.GetString("textPrompt");
        var objectName = arguments.GetString("objectName", "GeneratedModel")!;
        var pollIntervalMs = arguments.GetInt("pollIntervalMs", 5000)!.Value;
        var timeoutMs = arguments.GetInt("timeoutMs", 600_000)!.Value;

        try
        {
            // 1. Hyper3D Rodin連携が無効なら有効化する（UIクリック不要、Scene設定への直接書き込み）。
            var ensureResult = await EnsureHyper3DEnabledAsync(cancellationToken).ConfigureAwait(false);
            if (!ensureResult.Success)
            {
                return ensureResult;
            }

            // 2. 画像をBase64化してジョブを作成する。
            var images = new List<object>();
            foreach (var path in imagePaths)
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                var ext = Path.GetExtension(path) is { Length: > 0 } e ? e : ".png";
                images.Add(new object[] { ext, Convert.ToBase64String(bytes) });
            }

            var createParams = new { images, text_prompt = textPrompt };

            var createResult = await bridge.SendCommandAsync("create_rodin_job", createParams, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!createResult.TryGetProperty("uuid", out var uuidProp) ||
                !createResult.TryGetProperty("jobs", out var jobsProp) ||
                !jobsProp.TryGetProperty("subscription_key", out var subKeyProp))
            {
                return SkillActionOutcome.Fail($"Rodinジョブの作成応答が想定外の形式でした: {createResult.GetRawText()}");
            }

            var taskUuid = uuidProp.GetString()!;
            var subscriptionKey = subKeyProp.GetString()!;

            logger.LogInformation("Hyper3D Rodinジョブを作成しました。task_uuid={TaskUuid}", taskUuid);

            // 3. 完了までポーリングする。
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            IReadOnlyList<string> statusList = [];
            while (true)
            {
                var statusResult = await bridge.SendCommandAsync(
                    "poll_rodin_job_status",
                    new { subscription_key = subscriptionKey },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (statusResult.TryGetProperty("status_list", out var statusListProp) && statusListProp.ValueKind == JsonValueKind.Array)
                {
                    statusList = statusListProp.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .ToList();
                }

                if (statusList.Count > 0 && statusList.All(s => string.Equals(s, "Done", StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                if (statusList.Any(s => string.Equals(s, "Failed", StringComparison.OrdinalIgnoreCase)))
                {
                    return SkillActionOutcome.Fail($"Hyper3D Rodinの生成ジョブが失敗しました。status_list={string.Join(",", statusList)}", new { taskUuid, statusList });
                }

                if (DateTime.UtcNow > deadline)
                {
                    return SkillActionOutcome.Fail(
                        $"生成完了を待つ制限時間（{timeoutMs}ms）を超過しました。status_list={string.Join(",", statusList)}",
                        new { taskUuid, statusList });
                }

                await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
            }

            // 4. シーンにインポートする。
            var importResult = await bridge.SendCommandAsync(
                "import_generated_asset",
                new { name = objectName, task_uuid = taskUuid },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return SkillActionOutcome.Ok(new
            {
                taskUuid,
                statusList,
                import = JsonElementToPlainObject(importResult),
            }, $"画像から3Dモデル '{objectName}' の生成・インポートが完了しました。");
        }
        catch (BlenderBridgeException ex)
        {
            return SkillActionOutcome.Fail(ex.Message);
        }
    }

    /// <summary>JsonElement を、呼び出し側でそのままJSONシリアライズし直せる素朴なオブジェクトグラフに変換する。</summary>
    private static object? JsonElementToPlainObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToPlainObject(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToPlainObject).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };
}
