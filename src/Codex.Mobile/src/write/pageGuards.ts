import { platform } from '@codex/core/platform';

// What the WebView may load and who may talk to the app. The only page that belongs in it is the server's editor page.

export const editorPageUrl = (): string => `${platform().apiOrigin()}/mobile-editor.html`;

/** Whether `url` is the editor page (the WebView reports its own address with every message). */
export function isEditorPage(url: string | undefined): boolean {
  if (url === undefined) return false;
  const page = editorPageUrl();
  return url === page || url.startsWith(`${page}?`) || url.startsWith(`${page}#`);
}

export type NavigationDecision = 'allow' | 'external' | 'block';

/** The editor page itself loads; any other web link opens in the system browser; everything else is refused. */
export function decideNavigation(url: string): NavigationDecision {
  if (isEditorPage(url) || url === 'about:blank') return 'allow';
  return /^https?:\/\//i.test(url) ? 'external' : 'block';
}
