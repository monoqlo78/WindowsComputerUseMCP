# WindowsComputerUseMCP

GitHub Copilot などの MCP (Model Context Protocol) クライアントから、Windows 上の**任意のデスクトップアプリケーション**を
人間と同じ手段（画面認識・マウス・キーボード・UI Automation）で操作するためのローカル MCP サーバーです。
特定アプリ専用のコードは持たず、汎用的な「Computer Use」ツール群として実装しています。

最終目標は、Clipchamp の動画編集、Blender の 3D 操作、Adobe 製品の操作など、UI Automation だけでは
完結しない複雑なアプリケーションも、`screen_capture` + 座標操作 + 画面差分検出を組み合わせて
人間のように自在に操作できるようにすることです（詳細は [ROADMAP.md](ROADMAP.md) を参照）。

## 構成

| プロジェクト | 役割 |
|---|---|
| `WindowsComputerUseMCP.Core` | MCP/Windows実装に依存しない共通モデル・インターフェース・設定 |
| `WindowsComputerUseMCP.Safety` | 緊急停止・安全ポリシー（危険操作の承認要求・拒否判定） |
| `WindowsComputerUseMCP.Windows` | Win32 P/Invoke・UI Automation (FlaUI)・SendInput の実装 |
| `WindowsComputerUseMCP.Skills` | アプリ別「スキルパック」フレームワーク（Blender専用APIブリッジ、Adobe/Clipchamp向け汎用UIA操作など） |
| `WindowsComputerUseMCP.Server` | MCP stdio サーバー本体（ツール登録・DI・ホスティング） |
| `WindowsComputerUseMCP.ControlPanel` | 常時状態確認・緊急停止操作・監査ログ閲覧用の WPF アプリ |
| `WindowsComputerUseMCP.Tests` | 単体テスト |

詳細な設計は [ARCHITECTURE.md](ARCHITECTURE.md)、安全設計は [SECURITY.md](SECURITY.md) を参照してください。

## 必要環境

- Windows 10/11
- **.NET 10 SDK**
- Visual Studio 2022 (17.14+) または VS Code + GitHub Copilot 拡張機能（MCP クライアントとして利用する場合）

## ビルド・テスト

```powershell
dotnet build
dotnet test
```

## MCP クライアントへの登録方法（VS Code / GitHub Copilot）

ビルド後、`src\WindowsComputerUseMCP.Server\bin\Debug\net10.0-windows\WindowsComputerUseMCP.Server.exe` が
stdio ベースの MCP サーバーとして起動できます。

### ワークスペース単位で登録する場合

プロジェクト直下（または任意のワークスペース）に `.vscode/mcp.json` を作成します。

```json
{
  "servers": {
    "windows-computer-use": {
      "type": "stdio",
      "command": "C:\\path\\to\\WindowsComputerUseMCP\\src\\WindowsComputerUseMCP.Server\\bin\\Debug\\net10.0-windows\\WindowsComputerUseMCP.Server.exe"
    }
  }
}
```

### ユーザープロファイル単位で登録する場合

コマンドパレットから **MCP: Open User Configuration** を実行し、同様の `servers` エントリを追加します。
全ワークスペースで共通のサーバーを使いたい場合はこちらを利用してください。

登録後、VS Code の Chat ビューでツールの利用を許可すると、`window_list` や `screen_capture` などの
ツールがエージェントから呼び出せるようになります。

> **注意**: 本サーバーはローカルマシンのマウス・キーボード・画面を直接操作します。信頼できるプロンプト・
> エージェントからのみ利用し、[SECURITY.md](SECURITY.md) の安全設計（緊急停止・承認要求・監査ログ）を
> 必ず確認してください。

## 提供する MCP ツール

| ツール名 | 概要 |
|---|---|
| `system_get_capabilities` | OSバージョンやサーバーの機能可否を取得（読み取り専用） |
| `window_list` | 開いているウィンドウの一覧を取得 |
| `window_focus` | 指定したウィンドウを前面化 |
| `screen_capture` | 画面全体または指定ウィンドウのスクリーンショットを取得 |
| `ui_get_tree` | UI Automation ツリーを取得 |
| `ui_find` | 条件に一致する UI 要素を検索 |
| `ui_invoke` | UI 要素に対して Invoke/Toggle 等のパターン操作を実行 |
| `mouse_move` / `mouse_click` / `mouse_drag` / `mouse_scroll` | 座標ベースのマウス操作 |
| `keyboard_type_text` / `keyboard_press` / `keyboard_hotkey` | キーボード入力操作 |
| `wait_for_screen_change` | 操作前後の画面差分を検出し、変化を待機・確認 |

すべての入力系ツールは Safety 層のポリシー判定（許可アプリ／危険操作の承認要求／緊急停止）を経由します。

## アプリ別スキルパック（Blender / Adobe / Clipchamp など）

汎用ツールだけでは非効率、または対象アプリに専用の連携APIが存在するケースに対応するため、
「アプリごとの知識（スキル）」と「操作コード」を1セットにした **スキルパック** の仕組みを用意しています。

| ツール名 | 概要 |
|---|---|
| `skill_list_apps` | スキルパックが登録されている全アプリの一覧を取得（appId・表示名・対象プロセス名） |
| `skill_list_actions` | 指定アプリが提供するアクション一覧と、各パラメーターの説明を取得 |
| `skill_run_action` | 指定アプリの指定アクションを実行（引数はJSON文字列で渡す） |

### 現在登録済みのスキルパック

- **`blender`**: Blender本体のGPU描画ビューポートは UI Automation から中身が見えないため、
  Blender側にインストールした「BlenderMCP」アドオン（TCPソケット、既定ポート9876）に対して
  Python API呼び出しを直接送信する専用ブリッジを実装しています。
  `scene_info` / `execute_code` / `viewport_screenshot` に加え、1枚以上の画像から
  Hyper3D Rodin連携で3Dモデルを生成しシーンへインポートするまでを1コールで行う
  `generate_3d_from_image`（Hyper3D有効化→ジョブ作成→完了待ち→インポートを自動化）を提供します。
- **`clipchamp` / `photoshop` / `premiere` / `illustrator` / `aftereffects`**: 専用APIを持たない
  Windowsアプリ向けの汎用UIAスキルパック。要素検索・UI AutomationのInvokePattern実行
  （非対応時は要素中心を物理クリックへ自動フォールバック）・ウィンドウ内相対座標クリック・
  テキスト入力・ホットキー送信・スクリーンショット取得を共通のアクション名で提供します。
  Adobe製品やClipchampなど、UIAの対応範囲がアプリごとに異なる場合でも同じインターフェースで
  「まずAPI/UIA、無ければ画面操作」というフォールバック戦略を統一的に扱えます。

新しいアプリを追加する場合は、専用APIがあれば `WindowsComputerUseMCP.Skills` 配下に新しい
`ISkillPack` 実装を追加し、専用APIが無ければ `Generic/BuiltInAppDefinitions.cs` に対象プロセス名を
1行追加するだけで、上記の汎用アクション一式がそのアプリでも使えるようになります。

### Clipchamp・Teams・VS Code等、WebView2/Electron系アプリを操作する場合の注意

これらのアプリは実際のUIをWeb技術（HTML/DOM）で構築しており、Windowsのウィンドウ階層上は
`msedgewebview2`（または同等のEdge WebView2/Chromiumホスト）プロセスの子ウィンドウとして
UIがホストされています。`window_list` にはアプリ本体のウィンドウと、内部のWebView2ホスト
ウィンドウの両方が個別に表示されることがあります。

- Web系UIはDOM構造上ネストが深く、既定の `maxDepth`（`ui_get_tree`は8、`ui_find`は12）では
  メニューバー付近までしか到達できず、実際に操作したいボタン（例:「新しいビデオを作成」）が
  ツリーに現れないことがあります。**`maxDepth: 20`以上を明示的に指定することを推奨**します。
- `ui_get_tree`/`ui_find` はいずれもWebView2でホストされたコンテンツを正しく走査できます
  （UI Automationの `IsOffscreen` 誤検知に対する回避処理を実装済み）。

## Blenderなど3D/CADツールについて

Blenderのような3Dビューポート中心のアプリは、キャンバス部分がGPU描画されておりUI Automationからは
中身が見えません。そのため点単位での3D作成状況の確認・支援には、汎用UI操作ではなく上記の
**`blender` スキルパック**（BlenderMCPアドオン経由でのPython API直接実行）を使用してください。
`skill_run_action` 経由で `scene_info` / `execute_code` / `viewport_screenshot` /
`generate_3d_from_image` などが呼び出せます。事前に Blender 側で BlenderMCP アドオンを有効化し、
ソケットサーバー（既定ポート9876）を起動しておく必要があります。

## 緊急停止（Emergency Stop）

- **グローバルホットキー**: 既定 `Ctrl+Shift+F12`（`appsettings.json` の `Safety.EmergencyStopHotkey` で変更可能）。
  Server プロセスが起動している間、フォーカスに関係なく OS 全体で有効です。
- **ControlPanel（WPF）アプリ**からも「緊急停止」「解除」ボタンで同じ状態を切り替えられます。
  ControlPanel と Server は名前付きパイプ（`WindowsComputerUseMCP.ControlPanel.v1`）で通信するため、
  どちらのプロセスを先に起動しても問題ありません（Server 未起動時は「サーバー未接続」と表示されます）。
- 緊急停止が有効な間、`mouse_*` / `keyboard_*` / `ui_invoke` などの入力系ツールはすべて拒否されます。
  `window_list` / `screen_capture` など読み取り系ツールは、状況確認と解除操作を妨げないため継続して利用できます。

ControlPanel は次のようにビルド・起動します。

```powershell
dotnet build src\WindowsComputerUseMCP.ControlPanel
.\src\WindowsComputerUseMCP.ControlPanel\bin\Debug\net10.0-windows\WindowsComputerUseMCP.ControlPanel.exe
```

## 監査ログ

すべての操作は `%LOCALAPPDATA%\WindowsComputerUseMCP\Logs\audit-*.jsonl`（日次ローテーション）に記録されます。
ControlPanel の「監査ログ」テーブルからも最新の記録を確認できます。入力文字列は既定でマスクされ、
文字数・ハッシュ値のみが記録されます（詳細は [SECURITY.md](SECURITY.md) を参照）。

## 現在のステータス

フェーズ1〜7が完了しています（[ROADMAP.md](ROADMAP.md) 参照）。基本ツール群・入力操作・画面差分検出・
緊急停止 UI まで一通り実装・動作確認済みです。Clipchamp / Blender / Adobe 製品のような、UI Automation だけでは
完結しないアプリへの本格対応は、既存の汎用ツールの組み合わせで到達可能かを検証する継続課題です。
