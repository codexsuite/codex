import { expect, test } from '@playwright/experimental-ct-react';
import type { Page, WebSocketRoute } from 'playwright';
import currentUser from '../../../Codex.TestSupport/Fixtures/current-user.json' with { type: 'json' };
import library from '../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import manuscript from '../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { AppRoutes } from '../AppRoutes';
import type { AccessChanged } from '@codex/core/api/events';
import { editingTestIds } from '../editing/testIds';
import { writeTestIds } from '../features/write/testIds';
import { proseTestIds } from '../prose/testIds';
import { allBooks, headerFor, stubShellApi } from '../test-support/shellStubs';
import { shellTestIds } from '../shell/shellTestIds';
import { testIds } from '../testIds';

// AccessChanged (#80) reaching an open tab through the real SignalR client: the hub is played by a WebSocket route
// speaking SignalR's JSON protocol (records end with 0x1e).
const book = allBooks(library)[0];
const [first] = outline.acts.flatMap((a) => a.chapters).flatMap((c) => c.scenes);
const owner = { id: '0199c0de-0000-7000-8000-000100000009', displayName: 'Mara Lind' };
const RS = '\u001e';

// What the server says the role is now: the header's refetch after the event reads it.
let role: string | null = 'write';

// Serves the Write screen at `write` and stands in for the hub; resolves the hub's socket once the book is joined.
async function openWrite(page: Page): Promise<() => Promise<WebSocketRoute>> {
  role = 'write';
  await stubShellApi(page, library);
  await page.route(`**/api/books/${book.id}`, (route) =>
    role === null
      ? route.fulfill({ status: 404, json: { error: 'Not found.', code: 'not_found' } })
      : route.fulfill({ json: { ...headerFor(book), role, owner } }),
  );
  await page.route(`**/api/books/${book.id}/outline`, (route) => route.fulfill({ json: outline }));
  await page.route(`**/api/books/${book.id}/manuscript`, (route) =>
    route.fulfill({ json: manuscript }),
  );
  // The shell stub refuses the hub, and its route would also catch the socket's upgrade request: this test is the hub.
  await page.unroute('**/hubs/**');
  await page.route('**/hubs/book/negotiate**', (route) =>
    route.fulfill({
      json: {
        negotiateVersion: 1,
        connectionId: 'c1',
        connectionToken: 't1',
        availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }],
      },
    }),
  );
  let joined: (socket: WebSocketRoute) => void;
  const ready = new Promise<WebSocketRoute>((resolve) => (joined = resolve));
  await page.routeWebSocket(/\/hubs\/book/, (socket) => {
    socket.onMessage((data) => {
      for (const record of String(data)
        .split(RS)
        .filter((r) => r !== '')) {
        const message = JSON.parse(record) as {
          type?: number;
          target?: string;
          invocationId?: string;
        };
        if (message.type === undefined) socket.send(`{}${RS}`);
        if (message.type === 1 && message.target === 'JoinBook') {
          socket.send(
            `${JSON.stringify({ type: 3, invocationId: message.invocationId, result: null })}${RS}`,
          );
          joined(socket);
        }
      }
    });
  });
  // The socket route takes effect from the next document on (Playwright installs it as a page loads), and a
  // component test's page has already loaded: load it again before mounting.
  await page.reload();
  return () => ready;
}

const send = (socket: WebSocketRoute, next: AccessChanged['role'], userId = currentUser.id) => {
  if (userId === currentUser.id) role = next;
  const event: AccessChanged = { type: 'accessChanged', bookId: book.id, userId, role: next };
  socket.send(`${JSON.stringify({ type: 1, target: 'event', arguments: [event] })}${RS}`);
};

const editor = (page: Page) =>
  page.getByTestId(writeTestIds.scene(first.id)).getByTestId(proseTestIds.editor);

test('AccessChanged_WhenLoweredToView_ShouldTurnTheTabReadOnlyWithoutAReload', async ({
  mount,
  page,
}) => {
  const hub = await openWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  await expect(editor(page)).toHaveAttribute('contenteditable', 'true');
  const socket = await hub();

  send(socket, 'view');

  await expect(editor(page)).toHaveAttribute('contenteditable', 'false');
  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveText('View only');
  await expect(page.getByTestId(editingTestIds.toast).last()).toHaveText(
    /Mara Lind changed your access to Can view\. This tab is read-only now\./,
  );

  send(socket, 'write');

  await expect(editor(page)).toHaveAttribute('contenteditable', 'true');
  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveCount(0);
  await expect(page.getByTestId(editingTestIds.toast).last()).toHaveText(
    /You can edit this book now\. Typing works again\./,
  );
});

test('AccessChanged_WhenForAnotherUser_ShouldBeIgnored', async ({ mount, page }) => {
  const hub = await openWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  // The avatar's /api/auth/me has answered, so the tab knows who it is.
  await expect(page.getByTestId(shellTestIds.avatar)).toHaveText('JD');
  const socket = await hub();

  send(socket, 'view', '0199c0de-0000-7000-8000-0000000000ff');
  send(socket, 'comment');

  await expect(page.getByTestId(testIds.shell.roleChip)).toHaveText('Comment only');
  await expect(page.getByTestId(editingTestIds.toast)).toHaveCount(1);
});

test('AccessChanged_WhenRemoved_ShouldGoBackToTheLibraryWithAToast', async ({ mount, page }) => {
  const hub = await openWrite(page);
  await mount(<AppRoutes />, { hooksConfig: { route: `/books/${book.id}/write` } });
  const socket = await hub();

  send(socket, null);

  await expect(page.getByTestId(testIds.shell.libraryPage)).toBeVisible();
  await expect(page.getByTestId(writeTestIds.page)).toHaveCount(0);
  await expect(page.getByTestId(editingTestIds.toast).last()).toHaveText(
    new RegExp(`Mara Lind stopped sharing “${book.title}” with you\\.`),
  );
});
