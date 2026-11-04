// What react-native-webview puts on the page it loads.
interface Window {
  ReactNativeWebView?: { postMessage(message: string): void };
}
