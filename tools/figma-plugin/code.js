// WindowsComputerUseMCP Bridge - Figmaプラグイン側実装
//
// 本サーバー（AIエージェント側）が開いている ws://127.0.0.1:9877 のWebSocketサーバーへ接続し、
// 受信したコマンドをFigma Plugin API (figma.*) で実行して結果を返す「実行エージェント」。
// Blenderのアドオンとは接続の向きが逆（Figma側からサーバーへ接続しにいく）点に注意。
//
// メッセージ形式:
//   受信: {"requestId": "...", "type": "<コマンド名>", "params": {...}}
//   送信: {"requestId": "...", "result": ...} または {"requestId": "...", "error": "..."}

const BRIDGE_URL = "ws://127.0.0.1:9877/";
const RECONNECT_DELAY_MS = 3000;

let socket = null;

function log(...args) {
  console.log("[WindowsComputerUseMCP]", ...args);
}

function connect() {
  try {
    socket = new WebSocket(BRIDGE_URL);
  } catch (err) {
    log("WebSocket作成に失敗しました。再接続します。", err);
    scheduleReconnect();
    return;
  }

  socket.onopen = () => {
    log("ブリッジサーバーに接続しました。");
  };

  socket.onmessage = async (event) => {
    let message;
    try {
      message = JSON.parse(event.data);
    } catch (err) {
      log("受信メッセージの解析に失敗しました。", err);
      return;
    }

    const { requestId, type, params } = message;
    try {
      const result = await handleCommand(type, params || {});
      send({ requestId, result: result === undefined ? null : result });
    } catch (err) {
      send({ requestId, error: (err && err.message) || String(err) });
    }
  };

  socket.onclose = () => {
    log("ブリッジサーバーとの接続が切断されました。再接続を試みます。");
    scheduleReconnect();
  };

  socket.onerror = (err) => {
    log("WebSocketエラーが発生しました。", err);
  };
}

function scheduleReconnect() {
  socket = null;
  setTimeout(connect, RECONNECT_DELAY_MS);
}

function send(payload) {
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(payload));
  }
}

function hexToRgb(hex) {
  const cleaned = String(hex || "").replace("#", "");
  if (cleaned.length !== 6) {
    throw new Error(`色の指定が不正です（'#RRGGBB' 形式で指定してください）: ${hex}`);
  }
  const value = parseInt(cleaned, 16);
  return {
    r: ((value >> 16) & 0xff) / 255,
    g: ((value >> 8) & 0xff) / 255,
    b: (value & 0xff) / 255,
  };
}

function serializeNode(node) {
  const base = {
    id: node.id,
    type: node.type,
    name: node.name,
  };
  if ("x" in node) base.x = node.x;
  if ("y" in node) base.y = node.y;
  if ("width" in node) base.width = node.width;
  if ("height" in node) base.height = node.height;
  return base;
}

function applySolidFill(node, fillColorHex) {
  if (!fillColorHex) return;
  const { r, g, b } = hexToRgb(fillColorHex);
  node.fills = [{ type: "SOLID", color: { r, g, b } }];
}

async function handleCommand(type, params) {
  switch (type) {
    case "get_document_info": {
      const page = figma.currentPage;
      return {
        name: figma.root.name,
        currentPage: page.name,
        pageCount: figma.root.children.length,
        selectionCount: page.selection.length,
      };
    }

    case "list_pages": {
      return figma.root.children.map((p) => ({ id: p.id, name: p.name }));
    }

    case "get_selection": {
      return figma.currentPage.selection.map(serializeNode);
    }

    case "create_rectangle": {
      const rect = figma.createRectangle();
      rect.x = params.x || 0;
      rect.y = params.y || 0;
      rect.resize(params.width, params.height);
      if (params.name) rect.name = params.name;
      applySolidFill(rect, params.fillColorHex);
      figma.currentPage.appendChild(rect);
      figma.currentPage.selection = [rect];
      return serializeNode(rect);
    }

    case "create_ellipse": {
      const ellipse = figma.createEllipse();
      ellipse.x = params.x || 0;
      ellipse.y = params.y || 0;
      ellipse.resize(params.width, params.height);
      if (params.name) ellipse.name = params.name;
      applySolidFill(ellipse, params.fillColorHex);
      figma.currentPage.appendChild(ellipse);
      figma.currentPage.selection = [ellipse];
      return serializeNode(ellipse);
    }

    case "create_text": {
      const text = figma.createText();
      // Figmaはテキストノードのcharacters設定前にフォントロードが必須。
      await figma.loadFontAsync({ family: "Inter", style: "Regular" });
      text.x = params.x || 0;
      text.y = params.y || 0;
      text.fontSize = params.fontSize || 16;
      text.characters = params.characters || "";
      applySolidFill(text, params.fillColorHex);
      figma.currentPage.appendChild(text);
      figma.currentPage.selection = [text];
      return serializeNode(text);
    }

    case "set_fill_color": {
      const node = figma.getNodeById(params.nodeId);
      if (!node) throw new Error(`ノードが見つかりません: ${params.nodeId}`);
      applySolidFill(node, params.fillColorHex);
      return { updated: true };
    }

    case "delete_node": {
      const node = figma.getNodeById(params.nodeId);
      if (!node) throw new Error(`ノードが見つかりません: ${params.nodeId}`);
      node.remove();
      return { deleted: true };
    }

    case "export_node_image": {
      const node = params.nodeId ? figma.getNodeById(params.nodeId) : figma.currentPage.selection[0];
      if (!node) throw new Error("書き出し対象のノードが指定されておらず、選択もされていません。");
      const format = (params.format || "PNG").toUpperCase();
      const bytes = await node.exportAsync({
        format,
        constraint: { type: "SCALE", value: params.scale || 1 },
      });
      // Base64エンコード（Figmaプラグイン環境にはbtoa相当が無いため手動実装）。
      const base64 = figmaUint8ToBase64(bytes);
      return { nodeId: node.id, format, base64 };
    }

    case "execute_code": {
      // 信頼できるコードのみを想定。AsyncFunctionとしてfigmaコンテキストで実行する。
      const fn = new Function(
        "figma",
        "params",
        `return (async () => { ${params.code} })();`
      );
      return await fn(figma, params);
    }

    default:
      throw new Error(`未知のコマンドです: ${type}`);
  }
}

function figmaUint8ToBase64(bytes) {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  let result = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const b0 = bytes[i];
    const b1 = i + 1 < bytes.length ? bytes[i + 1] : 0;
    const b2 = i + 2 < bytes.length ? bytes[i + 2] : 0;
    result += chars[b0 >> 2];
    result += chars[((b0 & 3) << 4) | (b1 >> 4)];
    result += i + 1 < bytes.length ? chars[((b1 & 15) << 2) | (b2 >> 6)] : "=";
    result += i + 2 < bytes.length ? chars[b2 & 63] : "=";
  }
  return result;
}

connect();

// プラグインUIを表示しない（バックグラウンドで待受するだけ）。
// figma.showUI等は使わず、閉じるまで常駐させたい場合はユーザーが手動でメニューから再実行する。
figma.notify("WindowsComputerUseMCP Bridge: サーバーへの接続を開始しました。");
