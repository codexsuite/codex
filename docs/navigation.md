# Navigation: screens, routes, deep links, slots

Source of truth for URLs is `design/README.md` -> *Moving between screens*. Code lives in `src/Codex.Client/src/shell/`.

## Routes

| URL | Screen |
|---|---|
| `/` | Library. `/?q=<text>` searches; `/?book=new` opens the New book dialog (Library lane; with `#series=<id>` it is pre-filled for that series), `/?import=1` opens the Import dialog |
| `/#series=<id>` | Series screen (the Library page renders it) |
| `/books/:id` | Redirects to the screen last used in that book, else Plan |
| `/books/:id/{plan,write,references,layout,settings}` | Book screens inside the book workspace chrome |
| `/login?returnUrl=<path>` | Sign in (`features/auth`). The only page served without a session; `returnUrl` is followed only when it is a path on this site |
| `/account` | Your display name and password |
| `/account/password` | Change your password; every route lands here while a one-time password must be changed |
| `/settings/users` | User management, admins only |

`/mobile-editor.html` is not a screen of the SPA but a second page the server serves for the mobile app: the prose editor it shows in a WebView (the SPA's second Vite entry, `src/mobileEditor/`). It is a static file, served before authorization like `/login`'s assets, and needs no session: it holds no data and calls no API; the app hands it the text over the bridge (`@codex/core/editing/editorBridge`). Build the SPA (`pnpm run build`) or it answers 404.

Every page except `/login` and `/mobile-editor.html` renders inside `AuthGate` (`docs/auth.md`). A page load without a session is redirected by
the server to `/login?returnUrl=…`, and any API 401 sends the page there too (`@codex/core/api/client` calls the platform's `onUnauthorized`; `src/webPlatform.ts` redirects).

`AppRoutes.tsx` pre-registers one placeholder page per lane (`features/<lane>/<Lane>Page.tsx`); a lane replaces its page's body and never edits `AppRoutes.tsx`. An unknown book id (`GET /api/books/{id}` -> 404 `not_found`) shows an error state with a link back to the Library.

## Deep links

Hash parameters `#key=value&key2=value2`; a hash with no `=` is a bare target. Scenes and chapters use display numbers.

| Link | Meaning |
|---|---|
| `#scene=5.2`, `#chapter=5` | Scroll to and flash the scene or chapter (Plan, Write, Layout) |
| `#kb=<entry>` | Highlight a Knowledge base entry; beats the stored highlight |
| `#entry=<entry>` | Open the entry window |
| Write `&ref=<source>` | Open that marker's card |
| References `#<source>`, `#new` | Select a source; start a new one |
| References `#cite=<source>&title=<text>` (+ `&paraphrase=1`) | Cite a source from Write |
| `?return=<path>&sel=<selection>` | Where a detour comes back to (`return` must start with a single `/`) |
| Settings `#book`, `#series`, `#writing`, `#citations`, `#rules`, `#letters`, `#layout`, `#sharing`, `#archive` | Scroll to and flash that section (`#rules` and `#letters` sit inside Citations; `#rules` focuses the word limit) |

A malformed link (`#scene=abc`, `#chapter=5.2`, a bad `return`, an unknown bare word) is not a crash: `parseDeepLink` returns `{ ok: false, error }` and the page shows an error state (`errorBadLink`) with a link to the same screen without the target. Keys the shell does not know are ignored, so a lane can add its own.

### Link builders (`@codex/core/shell/links`)

Never write a Codex URL by hand; every "Open in ..." goes through these. `#20`'s `open_url` uses them too.

```ts
libraryLink(): string                      // '/'
seriesLink(seriesId): string               // '/#series=<id>'
newBookLink(): string                      // '/?book=new'
planLink(bookId, { scene?, chapter?, kb?, entry? })
writeLink(bookId, { scene?, chapter?, kb?, entry?, ref? })
referencesLink(bookId, { source?, isNew?, kb?, cite?, title?, paraphrase?, returnTo?, sel? })
layoutLink(bookId, { scene?, chapter? })
settingsLink(bookId, section?: SettingsSection)  // 'book' | 'series' | 'writing' | 'citations' | 'rules' | 'letters' | 'layout' | 'archive'
bookScreenLink(bookId, screen)             // plain '/books/<id>/<screen>'
```

### Reading the URL (`shell/useDeepLink.ts`)

```tsx
const deepLink = useDeepLink(); // { ok: true, link: DeepLink } | { ok: false, error: string }
if (deepLink.ok && deepLink.link.scene) scrollToScene(deepLink.link.scene);
```

The workspace already shows the error state for a bad link, so lane pages only render when `ok` is true. `parseDeepLink(pathname, search, hash)` is the pure form (vitest-tested with the builders as a round trip).

## Stores (`@codex/core/shell/stores`, `shell/useHighlight.ts`)

- `getLastScreen(bookId)` / `setLastScreen(bookId, screen)`: the platform's persistent store (web: `localStorage['codex.last.<book>']`). The workspace records every visit except Settings (never the screen a book opens on); a Library tile opens there, else the server's `lastScreen` hint, else Plan.
- `useHighlight(bookId, kbFromUrl?)` -> `[entryId | null, set]`: the platform's session store (web: `sessionStorage['codex.hl.<book>']`); `getHighlight`/`setHighlight` in the core, the hook in the SPA. A `kb` from the URL wins. Storage failures are swallowed (behaves as empty).

## Slots (`shell/Slots.tsx`)

The book bar (and the command palette) expose named places a lane fills from its own page, without editing a shell file.

| Slot | Where | Test id |
|---|---|---|
| `badge` | inside the References tab, after the count badge | `shell-slot-badge` |
| `scope` | after the tabs (scope selector, counts) | `shell-slot-scope` |
| `saveStatus` | right side of the bar | `shell-slot-save-status` |
| `format` | right side (Format popover button) | `shell-slot-format` |
| `focus` | right side (Focus button) | `shell-slot-focus` |
| `palette` | body of the command palette dialog | `shell-slot-palette` |

```tsx
import { SlotFill } from '../../shell/Slots';

export function WritePage() {
  return (
    <>
      <SlotFill slot="saveStatus">
        <SaveStatus onReload={reload} /> {/* src/editing: reads the one save-status store useAutosave drives */}
      </SlotFill>
      <SlotFill slot="palette">
        <PaletteContent />
      </SlotFill>
      {/* the page itself */}
    </>
  );
}
```

The References **count badge** is not a slot fill: the book bar draws it on every book screen from the header
(`referenceBadgeCount` and `referenceBadgeTone`: risks in danger, else sources to check in warn; hidden at 0;
test id `shell-references-badge`). After a write that changes clearance, invalidate `bookKey(bookId)` so it refreshes.
The **Layout tab badge** is drawn the same way (`shell-layout-badge`, a tinted danger chip, not a solid one): the issues that block print export, hidden at 0. For now that is the References risk count; Layout's preflight (#13) replaces it. On Plan, Write and Layout the bar also has a **sidebar toggle** (`shell-sidebar-toggle`, end of the book's identity) that shows or hides the Knowledge base sidebar, which starts shown on Plan and Write and hidden on Layout. Back and Settings are 22px icon buttons before the book's identity; the theme switch lives in the ⌘K palette. The bar ends with the current user's **avatar** (`shell-avatar`, initials from `GET /api/auth/me`); it has no ⌘K key cap. On Layout the stats read `48.2k words · ≈ 214 pages in Novel 6×9`, estimated from chapter word counts at the chosen template and trim (`features/layout/pageEstimate.ts`).
**Toasts:** the shell mounts the one `ToastHost`; call `showToast({ message, undo })` from `@codex/core/editing/toastStore`.

**Styling hooks:** CSS never selects by `data-testid`. The shell marks its regions with `data-shell-region` (`kb-sidebar`, `book-bar`) for a lane that restyles them (Write's Focus mode hides the sidebar and fades the bar).

`SlotFill` portals its children into the slot wherever it sits in the tree, and renders nothing if the slot is not on screen (the palette slot only exists while the palette is open). `SlotName` is `'badge' | 'scope' | 'format' | 'focus' | 'saveStatus' | 'palette'`.

## Command palette

`Ctrl+K` / `Cmd+K` anywhere toggles the palette; the Library bar's `⌘K` button (`PaletteButton`) opens it; Esc or a click on the backdrop closes it and focus returns to what had it. `usePalette()` -> `{ open, setOpen }`. The shell owns the frame; the Library lane fills the `palette` slot with books, series and actions.

## Knowledge base sidebar frame

`<KbSidebarFrame sections totalEntries status query onQueryChange onSelect onNewEntry highlighted />` (`shell/KbSidebarFrame.tsx`, fed by `features/kb/useKbSidebar`; a click sets the highlight store and `#kb=` and opens the entry window with `#entry=`, a click on the open entry closes it; the `+` on a category heading starts a new entry) is shown on Plan and Write. Entries are drag sources carrying `application/x-codex-entry` (`shell/dragPayload.ts`: `entryDragType`, `setEntryDragData`, `readEntryDragData`, payload `{ entryId, name }`). Drop targets call `readEntryDragData(event.dataTransfer)`.

## Theme

Tokens from `design/tokens.css` are mapped in `index.css` onto Tailwind's theme (`bg-surface`, `text-muted`, `border-border`, `text-sm`, `rounded-md`, `shadow-raised` ...). Light by default, OS preference unless `<html data-theme="light|dark">` forces it (`shell/theme.ts`, stored in `localStorage['codex.theme']`). Density: `<html data-density="comfortable">`. Inter comes from `@fontsource-variable/inter` ("Inter Variable").
