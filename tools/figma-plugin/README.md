# WindowsComputerUseMCP Bridge - Figma連携プラグイン

このプラグインは、WindowsComputerUseMCPサーバー（AIエージェント側）が開いているローカルの
WebSocketサーバー（`ws://127.0.0.1:9877/`）へ接続し、Figmaキャンバスへの操作コマンドを受け取って
実行する「ブリッジ」です。Figmaには外部プロセスから直接呼び出せる公式APIが無いため、この
開発用プラグインをFigmaデスクトップ版で実行しておくことで、AIエージェントがFigmaの
Plugin API（`figma.*`）を通じて正確にノード作成・色設定・エクスポート等を行えるようになります。

## セットアップ手順

1. Figmaデスクトップアプリを起動し、いずれかのファイルを開く。
2. メニューの `プラグイン` → `開発` → `マニフェストからプラグインをインポート...`
   （英語版: `Plugins` → `Development` → `Import plugin from manifest...`）を選択。
3. このフォルダ内の `manifest.json` を選択する。
4. `プラグイン` → `開発` → `WindowsComputerUseMCP Bridge` を実行する。
   - 実行すると画面下部に「WindowsComputerUseMCP Bridge: サーバーへの接続を開始しました。」という
     通知が表示されます。
   - WindowsComputerUseMCPサーバー（MCPサーバー本体）が起動していれば、この時点で
     `ws://127.0.0.1:9877/` へ自動接続されます。
5. AIエージェント（MCPクライアント）側から `figma` スキルパックのアクション
   （`is_connected` / `create_rectangle` / `set_fill_color` 等）を呼び出せば、
   実際にFigmaキャンバス上に反映されます。

## 注意事項

- このプラグインは**開発版プラグイン**として動作します。Figmaを再起動したり、対象ファイルを
  閉じたりするとプラグインの実行状態は失われるため、都度 `プラグイン` → `開発` から再実行してください。
- 接続が切れた場合は3秒ごとに自動再接続を試みます。
- `execute_code` アクションはFigmaのプラグインサンドボックス内で任意のJavaScriptコードを実行します。
  強力な反面、信頼できないコードを実行するとファイルを破壊しうるため、AIエージェント側での
  使用は慎重に行ってください。
- 本プラグインおよびWindowsComputerUseMCP全体は、リポジトリルートの `LICENSE.md` に従い
  **非商用利用限定**です。商用利用にはライセンス契約が必要です。
