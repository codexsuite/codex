import { screen, waitFor } from '@testing-library/react-native';
import { Linking } from 'react-native';
import { webViewHarness } from '~/test-support/mocks/reactNativeWebview';
import { loadRequest } from './test-support/events';
import { openWrite } from './test-support/openWrite';

describe('Write: the WebView only trusts the editor page', () => {
  it('OriginWhitelist_ShouldBeTheServerOnly', async () => {
    await openWrite();

    expect(webViewHarness.last().props.originWhitelist).toEqual(['https://codex.test']);
  });

  it('Navigation_ToTheEditorPage_ShouldBeAllowed', async () => {
    await openWrite();

    const allowed = webViewHarness
      .last()
      .props.onShouldStartLoadWithRequest?.(loadRequest('https://codex.test/mobile-editor.html'));

    expect(allowed).toBe(true);
  });

  it('Navigation_ToAnotherWebLink_ShouldOpenTheBrowserInstead', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue();
    open.mockClear();
    await openWrite();

    const allowed = webViewHarness
      .last()
      .props.onShouldStartLoadWithRequest?.(loadRequest('https://example.com/read-more'));

    expect(allowed).toBe(false);
    expect(open).toHaveBeenCalledWith('https://example.com/read-more');
  });

  it('Navigation_ToAnythingElse_ShouldBeRefused', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue();
    open.mockClear();
    await openWrite();

    const allowed = webViewHarness
      .last()
      .props.onShouldStartLoadWithRequest?.(loadRequest('javascript:alert(1)'));

    expect(allowed).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });

  it('Message_FromAnotherPage_ShouldBeDropped', async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => {});
    const { app, page } = await openWrite();
    await page.ready();
    await page.waitForLoads(1);

    await page.sayFrom(
      { type: 'change', sceneId: 's1', markdown: 'Injected.\n' },
      'https://evil.test/',
    );
    await page.sayFrom({ type: 'blur', sceneId: 's1' }, 'https://evil.test/');

    expect(app.api.callsTo('PUT /api/scenes/s1/prose')).toHaveLength(0);
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('https://evil.test/'));
  });

  it('Message_FromTheHarnessDefaultSender_ShouldBeDropped', async () => {
    jest.spyOn(console, 'warn').mockImplementation(() => {});
    await openWrite();

    await webViewHarness.last().emitFromPage({ type: 'ready', v: 1 });

    await waitFor(() => expect(console.warn).toHaveBeenCalled());
    expect(webViewHarness.last().sentToPage()).toEqual([]);
    expect(screen.getByTestId('mobile-write-webview')).toBeOnTheScreen();
  });
});
