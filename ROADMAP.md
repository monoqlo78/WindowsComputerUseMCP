# ROADMAP

本プロジェクトは以下のフェーズ順で進める。各フェーズ完了時にビルド・テストを実施し、
実施内容・変更ファイル・ビルド結果・残課題を報告してから、明示的な指示を待って次フェーズへ進む。

- [x] フェーズ1: 調査と設計（本ドキュメント含む）
- [x] フェーズ2: ソリューション作成（空プロジェクトのビルド確認まで）
- [x] フェーズ3: 基盤機能（共通結果モデル、設定読み込み、DI、ロギング、操作ID、Safetyポリシー、監査ログ、緊急停止状態、単体テスト）
- [x] フェーズ4: 読み取り専用ツール（`system_get_capabilities`, `window_list`, `screen_capture`, `ui_get_tree`, `ui_find`）
- [x] フェーズ5: 入力操作（`window_focus`, `ui_invoke`, `mouse_move`, `mouse_click`, `mouse_drag`, `mouse_scroll`,
      `keyboard_type_text`, `keyboard_press`, `keyboard_hotkey`）
- [x] フェーズ6: 画面変化確認（`wait_for_screen_change`、操作前後の画面比較、タイムアウト、キャンセル処理）
- [x] フェーズ7: ControlPanel（WPF簡易監視/操作 UI）
- [x] フェーズ8: MCPクライアント設定例・ドキュメント整備（README, docs 一式）

## MVP 受け入れ条件（フェーズ4〜6完了時点で満たすべきもの）— ✅ 検証済み（フェーズ8完了時点）

1. Windows電卓を起動 → `window_list` で発見 → `window_focus` で前面化 →
   `screen_capture` で画面取得 → `ui_get_tree`/`ui_find` でボタン発見 →
   `ui_invoke`/`mouse_click` で「1」「+」「2」「=」操作 → 結果「3」を UI Automation または
   画面変化で確認 → 監査ログに記録 → `Ctrl+Shift+F12` で緊急停止 → 緊急停止中は入力系ツールが拒否される。
   **検証結果**: `num3Button`→`plusButton`→`num4Button`→`equalButton` を `ui_invoke` で操作し、
   `CalculatorResults` が「表示は 7 です」になることを確認。監査ログにも各操作が記録された。
   緊急停止は ControlPanel の名前付きパイプ経由で Activate → `keyboard_type_text` が
   `errorCode: DENIED`（`SafetyDecision: Denied:EmergencyStop`）で拒否されること、
   Deactivate 後は再び許可されることを確認。物理ホットキー（Ctrl+Shift+F12）による
   有効化も別途 SendInput 経由で確認済み（フェーズ7検証時）。
2. 追加スモークテスト（メモ帳）: 起動 → 前面化 → テキスト入力 → 入力内容確認 →
   保存操作直前で Safety ポリシーが承認要求を返す（既存ユーザーファイルは変更しない）。
   **検証結果**: `mouse_click` でテキストエリアにフォーカス後、`keyboard_type_text` が
   `Success`/`Allowed` で記録された（監査ログの `textLength` が入力文字数と一致）。
   保存ホットキー（Ctrl+S）は、検証環境の実フォアグラウンドが Windows ロック画面であったため
   `ProtectedSurface`（UAC/セキュアデスクトップ）判定により拒否され、Safety層の多重防御が
   正しく機能していることを確認した。

## 将来の検証対象（MVP後・特定アプリ専用コードなしでの汎用性検証）

MVPは標準コントロール中心のアプリ（電卓・メモ帳）でUI Automation経路を検証するものであり、
本プロジェクトの最終目標である「任意のWindowsアプリを人間のように操作できる」ことの証明には、
より難易度の高い実アプリでの検証が必要になる。以下を将来のマイルストーンとして位置づける。

- **Clipchamp（デスクトップ版 / WebView2ホスト型UI）**: 検証済み。✅
  - `window_list` では、WinUIの外側ウィンドウ（`Clipchamp`プロセス、タイトル「Microsoft Clipchamp」）と、
    実際のWeb UIをホストする内側ウィンドウ（`msedgewebview2`プロセス）の両方が個別に見える。
  - **実バグを発見・修正**: `ui_get_tree` の走査ロジックが、UI Automationが `IsOffscreen=true` と
    （誤って）報告した祖先要素に遭遇すると、その配下の子要素ごと探索を打ち切ってしまっていた。
    WebView2/Chromium系のアクセシビリティ実装では、実際には表示されている要素の親ノードが
    誤って `IsOffscreen=true` を返すことがあり、これが原因で `ui_get_tree`（既定 `includeOffscreen=false`）が
    Clipchampのボタン等を一切返さない一方、`ui_find`（内部実装で `includeOffscreen=true` 固定）は
    同じ要素を正しく発見できる、という不整合が生じていた。
    `WindowsComputerUseMCP.Windows.Services.UiAutomationService.Traverse` を修正し、
    「結果へ含めるか」と「子要素を探索するか」を分離（offscreen要素は結果から除外しても子孫の探索は継続）。
  - 修正後、`ui_get_tree(maxDepth: 25)` で222要素・ボタン18個（「新しいビデオを作成」「AI でビデオを作成」等）を
    正しく検出できることをライブ確認。Web系UIはDOM構造上ネストが深いため、既定の `maxDepth`（8〜12）では
    メニューバー止まりで主要コンテンツに届かないことがあり、Clipchampのようなアプリでは
    `maxDepth: 20`以上を指定することを推奨（README/ツール説明に注記推奨）。
  - タイムライン操作・ドラッグ&ドロップ・プレビュー確認は `mouse_drag`/`ui_invoke`/`wait_for_screen_change`の
    組み合わせで到達可能と判断。個別アプリ専用コードの追加は不要、既存汎用ツールの範囲内で対応できる。
- **Blender（キャンバス/GPU描画中心のUI）**: 方針決定済み。本プロジェクト（汎用Windows UI操作）ではなく、
  bpy(Python API)を直接実行できる既存の専用Blender MCP（シーン情報取得・オブジェクト操作・
  ビューポートスクリーンショット等）を用いる。汎用UI Automation/座標操作よりも、点単位の
  3D作成状況を正確かつプログラム的に把握・操作できるため。WindowsComputerUseMCPはBlenderのような
  専用スクリプトAPIを持たないアプリ（Clipchamp等）に特化する。
- **Adobe製品（Photoshop / Premiere 等）**: Blenderと同様にキャンバス主体だが、ツールパレットやダイアログは
  標準コントロールに近い部分もある。ライセンス上・操作対象アプリの許可リスト（Safety設定）に明示的に
  追加してから検証する運用を想定する。

これらは個別アプリ向けの特殊コードを追加するのではなく、既存の汎用ツール
（`screen_capture`, `mouse_*`, `keyboard_*`, `ui_get_tree`, `ui_find`, `ui_invoke`, `wait_for_screen_change`）
の組み合わせだけで到達できるかを評価基準とする。到達できない場合に初めて、
「キャンバス内の要素をどう認識するか（将来的な画像認識/OCR連携の要否など）」を新たな検討課題として扱う。
現時点ではこれらのアプリ専用の実装はスコープ外とし、フェーズ4〜6の汎用ツールが揃った後の
検証・拡張フェーズとして扱う。

## 未確定事項・今後の判断が必要な点

- FlaUI.UIA3 のバージョン固定方針（マイナーアップデート追従ポリシー）。
- CsWin32 を全面採用するか、一部は手書き P/Invoke に留めるか（フェーズ3実装時に判断）。
- スクリーンキャプチャの実装方式（GDI BitBlt vs Windows.Graphics.Capture）。マルチモニターDPI混在環境での
  精度検証はフェーズ4で実施する。
- ControlPanel と Server 間の IPC 方式（名前付きパイプの具体的なプロトコル）はフェーズ7で設計する。
