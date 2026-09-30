// 데스크톱 창(WPF + WebView2, src/Astro.Desktop)과 주고받는 메시지.
// 브라우저에서 열었을 때는 창이 없으므로 아무 일도 하지 않는다.

interface HostWebView {
  postMessage(message: unknown): void
}

function host(): HostWebView | undefined {
  return (window as unknown as { chrome?: { webview?: HostWebView } }).chrome?.webview
}

/** 데스크톱 창 안에서 열렸는지 (브라우저면 false) */
export const inDesktop = () => host() !== undefined

/** 데스크톱 창에 메시지 보내기: { type: 'theme' | 'close', ... } (MainWindow.xaml.cs가 받는다) */
export function tellHost(message: { type: string } & Record<string, unknown>) {
  host()?.postMessage(message)
}

/** 앱 끄기 (데스크톱 창만) */
export const closeApp = () => tellHost({ type: 'close' })
