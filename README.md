<div align="center">
  <img src="assets/logo-256.png" alt="WindowsComputerUseMCP ロゴ" width="140">
  <h1>WindowsComputerUseMCP</h1>
  <p><strong>AI エージェントが Windows のデスクトップアプリを「見て・考えて・操作する」ための MCP サーバー</strong></p>
</div>

<p align="center">
  <img src="assets/banner.png" alt="1 つのエージェントから Blender・Illustrator/Figma・Clipchamp などを操作するイメージ" width="820">
</p>

GitHub Copilot などの MCP (Model Context Protocol) クライアントから、Windows 上の**任意のデスクトップアプリケーション**を
人間と同じ手段（画面認識・マウス・キーボード・UI Automation）で操作するためのローカル MCP サーバーです。
特定アプリ専用のコードは持たず、汎用的な「Computer Use」ツール群として実装しています。

最終目標は、Clipchamp の動画編集、Blender の 3D 操作、Adobe 製品の操作など、UI Automation だけでは
完結しない複雑なアプリケーションも、`screen_capture` + 座標操作 + 画面差分検出を組み合わせて
人間のように自在に操作できるようにすることです（詳細は [ROADMAP.md](ROADMAP.md) を参照）。

> **ライセンス**: 個人利用・非商用利用は無償です。商用利用には別途ライセンス契約が必要です。
> 詳細は [LICENSE.md](LICENSE.md) を参照してください。

## 構成

| プロジェクト | 役割 |
|---|---|
| `WindowsComputerUseMCP.Core` | MCP/Windows実装に依存しない共通モデル・インターフェース・設定 |
| `WindowsComputerUseMCP.Safety` | 緊急停止・安全ポリシー（危険操作の承認要求・拒否判定） |
| `WindowsComputerUseMCP.Windows` | Win32 P/Invoke・UI Automation (FlaUI)・SendInput の実装 |
| `WindowsComputerUseMCP.Skills` | アプリ別「スキルパック」フレームワーク（Blender専用APIブリッジ、Adobe/Clipchamp向け汎用UIA操作など） |
| `WindowsComputerUseMCP.Server` | MCP stdio サーバー本体（ツール登録・DI・ホスティング） |
| `WindowsComputerUseMCP.ControlPanel` | 常時状態確認・緊急停止操作・監査ログ閲覧用の WPF アプリ |
| `WindowsComputerUseMCP.DesktopLauncher` | 任意のコマンドを隠しデスクトップ上で起動する汎用ラッパー（stdio 素通し）。Playwright MCP などをバックグラウンド化する |
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

## MCP クライアントへの登録方法（GitHub Copilot / VS Code）

### 既定は未登録・未起動（必要なときだけ有効化）

**stdio MCP は、登録されたクライアントが接続・ツール発見のためにプロセスを起動する方式です。**
ツールを一度も呼ばなくても起動され、CLI/GUI の複数セッションがそれぞれ別プロセスを作る場合があります。
サーバー側には「このセッションで今後使われない」を判断する方法はありません。
未使用時の起動を防ぐには、**クライアントでサーバーを無効化するか、登録しない**でください。
ツールのチェックを外すだけ、またはプロセスを一度終了するだけでは再起動の防止になりません。

インストーラは**既定では**クライアント設定を変更せず、起動検証も行いません。
既存の登録は自動削除・無効化されないため、更新前に各クライアントで無効化してください。

### 配置・更新（クライアントの登録とは別操作）

```powershell
# ビルド → %LOCALAPPDATA% へ配置のみ。登録・起動・既存プロセスの強制停止はしない
.\scripts\install-mcp-server.ps1

# 明示的に、一時プロセスで MCP ハンドシェイクを検証する場合だけ指定
.\scripts\install-mcp-server.ps1 -Verify

# 利用者が明示的に Copilot CLI へ登録する場合だけ（接続時に起動される場合あり）
.\scripts\install-mcp-server.ps1 -UpdateClientConfig

# Playwright も必要な場合だけ追加指定
.\scripts\install-mcp-server.ps1 -UpdateClientConfig -RegisterPlaywright
```

更新先を使用するクライアントを先に無効化・終了してください。使用中ならスクリプトはエラーで停止します。
`dotnet publish -c Release` でサーバーとランチャーを一意の一時ディレクトリにビルドし、次の場所に配置します。

```
%LOCALAPPDATA%\WindowsComputerUseMCP\current\WindowsComputerUseMCP.Server.exe
%LOCALAPPDATA%\WindowsComputerUseMCP\current\launcher\wcu-desktop-launcher.exe
```

> **なぜ `bin\Debug` を直接登録してはいけないのか**
> MCP クライアントが起動したサーバープロセスは `bin` 配下の DLL をロックし続けます。
> その状態では `dotnet build` が失敗するため、ソースを更新してもクライアントは
> **古いバイナリを掴んだまま**になり、新しく追加したツールが一切現れません。
> publish 済みの独立したディレクトリを登録することで、この問題を回避できます。

更新は引数なしで行い、登録・有効化は必要時に明示してください。
既存の `-UpdateClientConfig` は維持しますが、登録するのは `windows-computer-use` のみです。
`playwright-bg` の追加・更新にはさらに `-RegisterPlaywright` が必要です。
既存の `-SkipPlaywright` / `-SkipVerify` は互換用に受け付け、どちらも省略が既定です。
明示登録時は設定のバックアップを保存し、他サーバーや既存の WCU 設定の追加プロパティを維持します。
`-Verify` は任意の一時起動であり、常駐登録は行いません。

### GitHub Copilot CLI：有効化・無効化

初回登録は**実際に使うときだけ**実行します（追加時に起動されることがあります）。

```powershell
copilot mcp add windows-computer-use -- "$env:LOCALAPPDATA\WindowsComputerUseMCP\current\WindowsComputerUseMCP.Server.exe"

# 使わない間はこちら。設定は保持し、有効化を明示するまで無効のまま
copilot mcp disable windows-computer-use
copilot mcp disable playwright-bg # こちらも登録している場合

# 次に必要になったときだけ
copilot mcp enable windows-computer-use
# playwright-bg は必要な場合だけ個別に enable する
```

対話モードでは `/mcp disable windows-computer-use` / `/mcp enable windows-computer-use` も利用できます。
別途起動済みの CLI/GUI セッションは閉じてから再開してください。古いクライアントに disable がない場合は、
`mcp-config.json` をバックアップして該当エントリを削除し、必要時に上記コマンドで再登録します。
設定ファイルは通常 `%USERPROFILE%\.copilot\mcp-config.json`（`COPILOT_HOME` 指定時はその配下）です。
本製品は非標準の `"disabled": true` などを勝手に書き込みません。

### VS Code：必要なワークスペースだけに手動登録

全ワークスペースに効くユーザー登録より、使用するワークスペースの `.vscode/mcp.json` を推奨します。
以下は**有効な登録例**であり、保存するとクライアントが起動する場合があります。

```json
{
  "servers": {
    "windows-computer-use": {
      "type": "stdio",
      "command": "C:\\Users\\<ユーザー名>\\AppData\\Local\\WindowsComputerUseMCP\\current\\WindowsComputerUseMCP.Server.exe"
    }
  }
}
```

未使用時は **MCP: List Servers** から対象サーバーを **Disable**、必要時に **Enable / Start** します。
`playwright-bg`、ユーザー設定・他アプリからの発見設定・他ワークスペースの登録も個別に確認してください。
`"chat.mcp.autostart": "never"` は補助設定ですが、Copilot の Agent Host が独立して開始する
セッションまでは抑止しないため、**登録の無効化・削除の代わりにはなりません**。

参照：[Copilot CLI の公式登録・管理手順](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers) /
[VS Code の管理・自動起動の説明](https://code.visualstudio.com/docs/agent-customization/mcp-servers)。

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
| `background_desktop_*` | ユーザーの画面に映らない「隠しデスクトップ」上でアプリを起動・操作（後述） |

すべての入力系ツールは Safety 層のポリシー判定（許可アプリ／危険操作の承認要求／緊急停止）を経由します。

## フォアグラウンド操作とバックグラウンド操作

「AIが操作している間、自分のPCが使えなくなるのでは？」という点は本MCPの重要な論点です。
実際には、**操作の種類によってフォアグラウンドが必要かどうかが異なります**。

### 1. もともとバックグラウンドで完結するもの（画面を奪わない）

| 手段 | 対象 | 理由 |
|---|---|---|
| スキルパックのAPIブリッジ | Blender（TCP 9876）、Illustrator（COM `DoJavaScript`）、Figma（WebSocket 9877） | アプリの公式スクリプティングAPIを直接叩くため、画面操作を一切伴わない |
| `ui_invoke` | UI Automation に対応したアプリ全般 | InvokePattern 等はメッセージベースで、前面化やフォーカスを必要としない |
| `screen_capture`（`windowHandle` 指定） | ほぼすべてのウィンドウ | `PrintWindow` + `PW_RENDERFULLCONTENT` により、背面や一部隠れたウィンドウも描画内容を取得できる |

つまり **「見る」と「APIで動かす」はすでにバックグラウンドで可能** です。

### 2. フォアグラウンドが必要なもの

`mouse_move` / `mouse_click` / `mouse_drag` / `mouse_scroll` / `keyboard_*` は
Win32 の `SendInput` を使用します。`SendInput` は**入力デスクトップ（＝ユーザーが実際に見ている画面）
のハードウェア入力キューに注入する**API であるため、対象ウィンドウが前面にある必要があります。

### 3. 「サブモニター（スクリーン2）でやらせる」では解決しない理由

Windows では、複数モニターを繋いでいても **1セッション＝1デスクトップ＝1つのカーソル・1つのフォーカス・
1つの入力キュー**です。モニターは同じデスクトップの表示領域が広がるだけなので、
サブモニターにウィンドウを置いてもマウスカーソルとフォーカスは共有されたままです。分離するには、

1. **同一セッション内の別デスクトップオブジェクト**（本MCPが採用）
2. 別の Windows セッション（RDP・ユーザー切り替え）
3. VM / Windows Sandbox

のいずれかが必要です。

### 4. `background_desktop_*` ツール（隠しデスクトップ方式）

Win32 の `CreateDesktopW` で**画面に表示されないデスクトップオブジェクト**を作り、
専用ワーカースレッドを `SetThreadDesktop` でそこに束縛して操作します。
アプリは `STARTUPINFO.lpDesktop` 指定で最初からその隠しデスクトップ上に起動されるため、
**ユーザーが見ている画面には一切現れません**。

| ツール名 | 概要 |
|---|---|
| `background_desktop_create` | 隠しデスクトップを作成 |
| `background_desktop_list` | 隠しデスクトップと、その上のウィンドウ数を一覧 |
| `background_desktop_launch` | 隠しデスクトップ上でアプリを起動 |
| `background_desktop_window_list` | 隠しデスクトップ上のウィンドウを列挙（通常の `window_list` では見えない） |
| `background_desktop_capture` | 隠しデスクトップ上のウィンドウをキャプチャ |
| `background_desktop_click` | 隠しデスクトップ上でクリック |
| `background_desktop_type_text` | 隠しデスクトップ上でテキスト入力 |
| `background_desktop_send_keys` | 隠しデスクトップ上でキー送信 |
| `background_desktop_switch` | 一時的に隠しデスクトップを画面に表示（確認用。既定で自動復帰） |
| `background_desktop_close` | 隠しデスクトップと起動したプロセスを終了 |

使用例（AIエージェントからの呼び出しイメージ）:

```jsonc
// 1. 隠しデスクトップを作る
{ "tool": "background_desktop_create", "args": { "name": "work1" } }

// 2. その上でアプリを起動（ユーザーの画面には出ない）
{ "tool": "background_desktop_launch",
  "args": { "desktopName": "work1", "executablePath": "notepad.exe" } }

// 3. ウィンドウを探して「見る」
{ "tool": "background_desktop_window_list", "args": { "desktopName": "work1" } }
{ "tool": "background_desktop_capture",
  "args": { "desktopName": "work1", "windowHandle": 7020710 } }

// 4. 「手を動かす」
{ "tool": "background_desktop_type_text",
  "args": { "desktopName": "work1", "windowHandle": 7020710, "text": "Hello" } }

// 5. 後片付け
{ "tool": "background_desktop_close", "args": { "desktopName": "work1" } }
```

#### 入力方式の自動切り替え

隠しデスクトップは入力デスクトップではないため `SendInput` が機能しません。
そこで本MCPは、対象デスクトップが入力デスクトップかどうかを実行時に判定し、
**非対話デスクトップでは `WM_CHAR` / `WM_KEYDOWN` / `WM_LBUTTONDOWN` などの
ウィンドウメッセージを直接ポストする方式へ自動的にフォールバック**します。
呼び出し側はツールの使い分けを意識する必要はありません。

#### 既知の制約（正直な注意点）

- **GPU描画に強く依存するアプリは動かない場合がある**
  Electron 系（Clipchamp、Teams、VS Code、Figmaデスクトップ版）や
  Blender のビューポートは、非対話デスクトップでは真っ白に描画される、
  あるいは起動自体に失敗することがあります。
  → これらは **スキルパックのAPIブリッジ（1.）を使う方が確実**です。
  なお **Chromium / Microsoft Edge は隠しデスクトップ上でも正常に描画される**ことを
  実測で確認済みです（後述の `wcu-desktop-launcher` はこれを利用しています）。
- **修飾キー同時押し（Ctrl+C 等）の再現性が下がる**
  メッセージベース入力では、アプリ側が `GetKeyState` で修飾キー状態を見る実装だと
  ホットキーが効かない場合があります。
  → メニュー操作は `ui_invoke`、またはアプリ固有のスキルパックを優先してください。
- **画面解像度・DPIは既存デスクトップを継承**し、任意に変更することはできません。
- 一部のインストーラは対話デスクトップを明示的に要求するため起動できません。

### 5. `wcu-desktop-launcher` — 他のMCPサーバーごとバックグラウンドへ追い出す

Playwright MCP のようにブラウザを起動する外部 MCP サーバーは、
そのままだと**ツールを呼ぶたびにブラウザが前面に出てフォーカスを奪います**。
これを解決するのが、本リポジトリに同梱の汎用ラッパー **`wcu-desktop-launcher.exe`** です。

```
wcu-desktop-launcher --desktop <デスクトップ名> [--cwd <作業ディレクトリ>] -- <コマンド> [引数...]
```

原理はシンプルで、Windows の子プロセスは
**`STARTUPINFO.lpDesktop` で指定されたデスクトップを継承する**という仕様を使います。
MCP サーバー本体（Node.js プロセス）を隠しデスクトップ上で起動すれば、
そこから起動されるブラウザも自動的に同じ隠しデスクトップに乗ります。

重要なのは、**デスクトップの割り当てはウィンドウと入力キューにしか影響せず、
標準入出力には一切影響しない**という点です。
ランチャーは自分の stdin / stdout / stderr をそのまま子へ継承させるため、
MCP の JSON-RPC (stdio) は素通しで流れます。ラッパーによる通信オーバーヘッドはありません。

さらにランチャーは `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` を設定したジョブオブジェクトに
子プロセスを登録します。MCP クライアントが切断してランチャーが終了すると、
**ブラウザを含む子プロセスツリーがまとめて確実に終了**し、
隠しデスクトップ上にゾンビプロセスが溜まりません。

#### 登録例（Playwright MCP をバックグラウンド化する）

インストーラは既定で `playwright-bg` を登録しません。必要な場合だけ `-UpdateClientConfig -RegisterPlaywright`
を指定するか、次の例を手動で追加してください。
パスは実際の絶対パスに置き換えてください（クライアントが `%LOCALAPPDATA%` を展開するとは限りません）。
追加によりクライアント起動時にランチャーや Node が動く場合があるため、未使用時は上記の方法で無効化します。

```jsonc
{
  "mcpServers": {
    "playwright-bg": {
      "type": "local",
      "command": "C:\\Users\\<ユーザー名>\\AppData\\Local\\WindowsComputerUseMCP\\current\\launcher\\wcu-desktop-launcher.exe",
      "args": [
        "--desktop", "WCU_Background", "--",
        "cmd.exe", "/c", "npx", "-y", "@playwright/mcp@latest",
        "--browser", "msedge",
        "--config", "C:\\Users\\<ユーザー名>\\AppData\\Local\\WindowsComputerUseMCP\\current\\playwright-mcp-background.config.json"
      ],
      "tools": ["*"]
    }
  }
}
```

- `npx` はバッチファイル（`.cmd`）なので、`cmd.exe /c` を経由させる必要があります。
- `config/playwright-mcp-background.config.json` は
  `--start-fullscreen` を含まない固定サイズ（1920×1080）のブラウザ起動オプションです。
  フォアグラウンドを奪う設定を持ち込まないために必ず指定してください。

#### 注意点

- **Copilot CLI / VS Code の再起動が必要**です（設定は起動時にのみ読まれます）。
- 隠しデスクトップ上のブラウザも `background_desktop_capture` で目視確認できます。
- Playwright の操作は CDP 経由なので、OS の入力デスクトップに依存せず完全に動作します。

#### power-pages プラグインの Playwright もバックグラウンド化する

GitHub Copilot CLI の **power-pages プラグイン**は、`mcp-config.json` とは別に
独自の Playwright MCP を起動します。その既定設定は `--start-fullscreen` を含むため、
`playwright-bg` を登録しても**こちらが前面化してフォーカスを奪い続けます**。

同梱のパッチスクリプトで、プラグイン側も同じランチャー経由に切り替えられます。

```powershell
# 適用（冪等。適用済みなら何もしない）
.\scripts\patch-power-pages-playwright.ps1

# 元に戻す
.\scripts\patch-power-pages-playwright.ps1 -Revert
```

- 変更前の内容は `launch-playwright-mcp.js.orig` として保存されます。
- `wcu-desktop-launcher.exe` が見つからない場合や、環境変数
  `POWER_PAGES_PLAYWRIGHT_FOREGROUND=1` を設定した場合は**元の前面起動へ自動的に戻ります**。
- **プラグインが更新されると変更は失われます。** その場合は再実行してください。


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
- **`illustrator`**: Adobe Illustratorが長年公式に提供しているCOM自動化
  （ProgID `Illustrator.Application`）と、その上で動く ExtendScript（JavaScript）実行API
  （`Application.DoJavaScript`）を直接呼び出す専用ブリッジです。画面クリックを一切使わず、
  ドキュメント情報取得（`document_info`）、任意ExtendScript実行（`execute_script`）、
  矩形/楕円/テキスト作成（`create_rectangle` / `create_ellipse` / `create_text_frame`）、
  塗り色設定（`set_fill_color`）、保存/読み込み/PNG・JPG・PDF書き出し
  （`save_document` / `open_document` / `export_document`）を提供します。
  Illustratorが起動しCOM登録済みであることが前提（自動起動はしません）。
- **`figma`**: Figmaデスクトップ版はCOMや外部TCPサーバーのような公式連携APIを持たないため、
  同梱の開発用プラグイン（`tools/figma-plugin/`）をFigma側で実行し、そのプラグインが
  本サーバーの開くWebSocketサーバー（既定 `ws://127.0.0.1:9877/`、接続方向はBlenderと逆で
  Figma側から接続しにいく）へつなぎ込む方式でブリッジしています。
  ノード作成（`create_rectangle` / `create_ellipse` / `create_text`）、塗り色設定
  （`set_fill_color`）、削除（`delete_node`）、選択/ページ情報取得
  （`get_selection` / `list_pages` / `document_info`）、画像書き出し（`export_node_image`）、
  任意コード実行（`execute_plugin_code`）を提供します。事前に
  `tools/figma-plugin/README.md` の手順でプラグインをインポート・実行しておく必要があります
  （未接続時は `is_connected` で確認可能）。
- **汎用UIAスキルパック**: 専用APIを持たないWindowsアプリ向け。要素検索・UI Automationの
  InvokePattern実行（非対応時は要素中心を物理クリックへ自動フォールバック）・ウィンドウ内相対座標
  クリック・テキスト入力・ホットキー送信・スクリーンショット取得を共通のアクション名で提供します。
  UIAの対応範囲がアプリごとに異なる場合でも同じインターフェースで
  「まずAPI/UIA、無ければ画面操作」というフォールバック戦略を統一的に扱えます。
  現在 `Generic/BuiltInAppDefinitions.cs` に登録済みのアプリ:
  - 動画編集: `clipchamp`（Clipchamp）, `premiere`（Adobe Premiere Pro）,
    `aftereffects`（Adobe After Effects）, `davinciresolve`（DaVinci Resolve）, `capcut`（CapCut）
  - デザイン/画像編集: `photoshop`（Adobe Photoshop）,
    `indesign`（Adobe InDesign）, `lightroom`（Adobe Lightroom Classic）,
    `gimp`（GIMP）, `krita`（Krita）
  - 音声編集: `audition`（Adobe Audition）

  ※ `illustrator` と `figma` は専用スキルパック（上記）に昇格済みのため、このリストには含まれません。

新しいアプリを追加する場合は、専用APIがあれば `WindowsComputerUseMCP.Skills` 配下に新しい
`ISkillPack` 実装を追加し、専用APIが無ければ `Generic/BuiltInAppDefinitions.cs` に対象プロセス名を
1行追加するだけで、上記の汎用アクション一式がそのアプリでも使えるようになります。

> **注意**: `ProcessNames` は代表的なプロセス名の想定値です。実際にインストールされているバージョンや
> エディションによって実行ファイル名が異なる場合があります。対象アプリが `skill_run_action` で
> 見つからない場合は、タスクマネージャー等で実プロセス名を確認し、該当行を修正してください。

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

- **グローバルホットキー**: 従来どおり Server 起動中は `Ctrl+Shift+F12` を登録します
  （`Safety.EmergencyStopHotkey` で変更可能）。実操作中の安全機構は弱体化しません。
  同じキーを登録できるのは通常1プロセスだけであり、ホットキーはそのプロセスのみを停止します。
  複数セッションでは ControlPanel の全接続先への緊急停止を使用してください。
- **ControlPanel（WPF）アプリ**からも「緊急停止」「解除」ボタンで同じ状態を切り替えられます。
  Server はプロセス別パイプ `WindowsComputerUseMCP.ControlPanel.v1.<WindowsセッションID>.<PID>` を公開し、
  ControlPanel は同一ユーザー・Windows セッションの検出済みサーバーすべてに要求を送ります。
  1件でも失敗すれば正常表示にはせず、混在状態は「一部または全サーバー」と表示します。
  Server 未起動時は「サーバー未接続」であり、ControlPanel が Server を起動することはありません。
  新たに起動したプロセスには過去の停止状態は引き継がれません。
  JSON の要求・応答形式は従来どおりで、新 ControlPanel は旧固定名パイプも検出しますが、
  旧 ControlPanel は新プロセス別パイプを検出できません。両方を更新してください。
- 緊急停止が有効な間、`mouse_*` / `keyboard_*` / `ui_invoke` などの入力系ツールはすべて拒否されます。
  `window_list` / `screen_capture` など読み取り系ツールは、状況確認と解除操作を妨げないため継続して利用できます。

ControlPanel は次のようにビルド・起動します。

```powershell
dotnet build src\WindowsComputerUseMCP.ControlPanel
.\src\WindowsComputerUseMCP.ControlPanel\bin\Debug\net10.0-windows\WindowsComputerUseMCP.ControlPanel.exe
```

### 常駐機能の明示的な有効化

Figma WebSocket リスナー（ポート9877）は既定無効です。必要な場合だけ
`%LOCALAPPDATA%\WindowsComputerUseMCP\usersettings.json` に指定し、対象 MCP セッションを再起動してください。
以下は Figma 連携を有効にする例です。

```json
{
  "WindowsComputerUseMcp": {
    "BackgroundServices": {
      "EnableFigmaBridge": true
    }
  }
}
```

無効時も読み取りや通常のツールは使え、緊急停止ホットキー・ControlPanel IPC の非同期待機・安全ポリシーは維持されます。
Figma プラグイン接続を使うアクションには `EnableFigmaBridge: true` が必要です。
ポートは1プロセスだけが所有するため、Figma を使うセッションのみで有効化してください。
クライアントの `env` には `WindowsComputerUseMcp__BackgroundServices__EnableFigmaBridge` を `"true"` として
指定することもできます。設定の優先順は配置先 appsettings → usersettings → 環境変数 → コマンドラインです。
ツール一覧取得だけで画面監視・隠しデスクトップ作成・ブラウザ起動は行いません。

## 監査ログ

すべての操作は `%LOCALAPPDATA%\WindowsComputerUseMCP\Logs\audit-*.jsonl`（日次ローテーション）に記録されます。
ControlPanel の「監査ログ」テーブルからも最新の記録を確認できます。入力文字列は既定でマスクされ、
文字数・ハッシュ値のみが記録されます（詳細は [SECURITY.md](SECURITY.md) を参照）。

## 実運用で得られた知見（トラブルシューティング）

実機での自動化検証で判明した、再現性のある落とし穴と対処法をまとめます。

### DPI スケーリングと座標系

本サーバーは `app.manifest` で **Per-Monitor V2 DPI Aware** を宣言しているため、
`screen_capture` / `window_list` / `mouse_*` はすべて**物理ピクセル座標**で一貫しています
（例: 150% スケーリング環境では論理 1664×1109 ではなく物理 2496×1664 を返します）。

一方で、**MCP の外で自作した補助スクリプト（PowerShell / Python など）は既定で DPI 非対応**です。
そのまま `GetSystemMetrics` や `mouse_event(MOUSEEVENTF_ABSOLUTE)` を使うと論理座標で正規化され、
カーソルが**スケール倍率のぶんだけズレた位置**に飛びます。補助スクリプト側では必ず冒頭で
DPI 対応を宣言してください。

```powershell
# 座標を扱う前に必ず呼ぶ（これが無いと 150% 環境で約1.5倍ズレる）
[void][YourInterop]::SetProcessDPIAware()
```

クリック後は `GetCursorPos` で「狙った座標にカーソルが到達したか」を検証すると、
座標計算の誤りとクリック対象の誤りを切り分けられます。

### 確実にフォアグラウンドを奪う方法

`SetForegroundWindow` の単体呼び出しは、Windows のフォアグラウンドロックによりほぼ失敗します。
`AttachThreadInput` で「自スレッド」「現在の前面ウィンドウのスレッド」「対象ウィンドウのスレッド」を
一時的に結合してから呼ぶと、確実に成功します。

```
AttachThreadInput(myTid, foregroundTid, true)
AttachThreadInput(myTid, targetTid,     true)
BringWindowToTop(hwnd); SetForegroundWindow(hwnd)
AttachThreadInput(myTid, targetTid,     false)
AttachThreadInput(myTid, foregroundTid, false)
```

`SetWindowPos(HWND_TOPMOST)` による最前面固定は、相手も topmost の場合（Tauri / Electron 製アプリなど）に
競合して安定しないため推奨しません。

### ブラウザ（Chrome 系）を操作する場合の制約

- **モダンな Chrome は windowless レンダラ**のため、`EnumChildWindows` で子ウィンドウが 0 件になり、
  `Chrome_RenderWidgetHostHWND` も存在しません。結果として **`ui_get_tree` / `ui_find` では
  Web コンテンツ（DOM）を一切取得できません**（ブラウザ自体の UI しか返りません）。
  Web ページの操作は **`screen_capture`＋座標クリック**か、Playwright 等の専用ツールを使ってください。
- **`screen_capture(windowHandle=...)` は Chrome では古いフレームを返すこと**があります
  （GPU コンポジット由来。特にウィンドウのサイズ変更直後）。
  Chrome が前面にある場合は **`screen_capture(monitorIndex=0)` を使う**のが確実です。
- **`chrome.exe --new-window <URL>` は既存インスタンスに合流**するため、
  ログイン済みセッションのままページを開けます。キーボード入力を伴わないぶん誤操作リスクがありません。
- 実行中の Chrome プロファイル（`Default\Network\Cookies` 等）は排他ロックされており、
  コピーによる「ログイン状態を引き継いだ別プロファイルでのバックグラウンド起動」はできません。
  ブラウザをバックグラウンド化したい場合は
  [`wcu-desktop-launcher`](#5-wcu-desktop-launcher--他のmcpサーバーごとバックグラウンドへ追い出す) を使ってください。

### ユーザーと画面を取り合わない

フォアグラウンド操作は、ユーザーが PC を使用している最中に実行すると相互に前面を奪い合って失敗します。
`GetLastInputInfo` でアイドル時間を確認し、**一定時間（例: 40 秒）操作がないことを確認してから**
一連の操作を**1本のスクリプトで一気に**実行すると成功率が大きく上がります
（ツール呼び出しを細かく往復すると、その隙間にユーザー操作が割り込みます）。

### 日本語を含む PowerShell スクリプト

Windows PowerShell 5.1 は **BOM なし UTF-8 を cp932 として読む**ため、日本語を含む `.ps1` は文字化けします。
判定用の文字列（ウィンドウタイトル等）は **ASCII のみ**にする（例: ダイアログはタイトルではなく
ウィンドウクラス `#32770` で判定する）か、**UTF-8 BOM 付きで保存**してください。

## 現在のステータス

フェーズ1〜7が完了しています（[ROADMAP.md](ROADMAP.md) 参照）。基本ツール群・入力操作・画面差分検出・
緊急停止 UI まで一通り実装・動作確認済みです。加えて、ユーザーの画面を占有せずに操作できる
**バックグラウンドデスクトップ（`background_desktop_*`）** を実装し、隠しデスクトップ上での
アプリ起動 → ウィンドウ列挙 → キャプチャ → テキスト入力 → 終了までを実機で検証済みです。
さらに **`wcu-desktop-launcher`** により、Playwright MCP のような外部 MCP サーバーごと
隠しデスクトップへ追い出す方式を実装し、ブラウザ操作がフォアグラウンドを奪わないことを実機検証済みです。
Clipchamp / Blender / Adobe 製品のような、UI Automation だけでは
完結しないアプリへの本格対応は、既存の汎用ツールの組み合わせで到達可能かを検証する継続課題です。

対応アプリのスキルパックは今後も継続的に拡充していく予定です（Clipchamp / Photoshop / Premiere Pro /
Illustrator / After Effects の実操作精度向上、DaVinci Resolve・CapCut・Figma 等への対応拡大など）。
新しいアプリへの対応リクエストや、既存スキルパックの改善案がある場合は Issue で管理してください。

## ロゴ・ブランドアセット

ロゴは Azure OpenAI (`gpt-image-2`) で生成し、`assets/` 配下にコミットしています。

| ファイル | 用途 |
|---|---|
| `assets/logo.png` | マスター（1024×1024、白背景） |
| `assets/logo-transparent.png` | 背景透過版（1024×1024） |
| `assets/logo-512.png` 〜 `logo-32.png` | 各サイズの透過 PNG（512 / 256 / 128 / 64 / 32） |
| `assets/app.ico` | Windows 実行ファイル用アイコン（16〜256px マルチ解像度）。ControlPanel の `ApplicationIcon` に設定済み |
| `assets/banner.png` | README ヘッダー用の横長バナー |
| `assets/avatar-460.png` | GitHub アカウント／Organization のアバター用（460×460、円形クロップに耐える構図） |
| `assets/candidates/` | 不採用のデザイン案（差し替え用に保持） |

> **GitHub 上のリポジトリアイコンについて**
> GitHub にはリポジトリ固有のアイコン設定がなく、リポジトリ名の左に表示される丸いアイコンは
> **オーナー（ユーザー / Organization）のアバター**です。したがって `assets/` に画像を push しても変わりません。
> 変更するには <https://github.com/settings/profile> で `assets/avatar-460.png` をアップロードします
> （アバターの設定は GitHub API では行えず、Web UI 操作が必須です）。
> 本リポジトリのオーナーアバターは `assets/avatar-460.png` を適用済みです。
> リポジトリの OGP 画像は Settings → Social preview から `assets/banner.png` を設定できます。

デザインの意図は、**ウィンドウ枠 ＝ Windows アプリ**、**回路で描かれたカーソル ＝ AI エージェントによる操作**です。
バナーでは 1 つのエージェントから 3D（Blender）・デザイン（Illustrator / Figma）・動画編集（Clipchamp）へ
接続が伸びる様子を表しています。

配色は次のとおりです。

| 役割 | カラー |
|---|---|
| ベース（濃紺） | `#021A52` |
| プライマリ（青） | `#0F7BF0` |
| アクセント（シアン） | `#2FEFFF` |

> ロゴは本プロジェクトの識別に用いるものです。ライセンス条件（非商用）はロゴにも同様に適用されます。

## ライセンス

本ソフトウェアは **個人利用・非商用利用に限り無償** で提供されます。
商用目的（企業内業務利用、製品への組み込み、受託開発等）で利用する場合は、
事前に著作権者との商用ライセンス契約が必要です。詳細・お問い合わせは
[LICENSE.md](LICENSE.md) を参照してください（連絡先: monoqlo78@gmail.com）。
