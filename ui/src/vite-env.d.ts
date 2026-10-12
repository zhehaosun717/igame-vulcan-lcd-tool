/// <reference types="vite/client" />

// Edge WebView2 宿主注入的最小接口（协议见 docs/bridge-protocol.md）
interface WebViewMessageEvent {
  data: string
}

interface WebView {
  postMessage(message: string): void
  addEventListener(
    type: 'message',
    listener: (event: WebViewMessageEvent) => void
  ): void
  removeEventListener(
    type: 'message',
    listener: (event: WebViewMessageEvent) => void
  ): void
}

interface Window {
  chrome?: {
    webview?: WebView
  }
}
