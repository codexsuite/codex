import { parseDeepLink } from '@codex/core/shell/deepLink';
import { writeLink } from '@codex/core/shell/links';
import { occurrencesUpTo } from './citePassage';

// The New source… detour (design/README.md, Write → New source; References → Return to Write). Write saves the scene,
// then opens References' Add source with `?return=<Write at the scene>&sel=<selection>`. Saving there comes back to
// Write with `?sel=…#scene=…&cite=<source>&title=<title>`, and Write cites the saved selection; Cancel comes back to
// the scene only.
//
// The selection is a range of the scene's stored Markdown at the version Write saved before leaving:
// `<start>-<end>@<version>`. Coming back to a scene whose prose has moved on since, the words are not cited.

export interface SavedSelection {
  start: number;
  end: number;
  version: number;
}

export const selectionText = ({ start, end, version }: SavedSelection) =>
  `${start}-${end}@${version}`;

export function parseSelection(text: string | undefined): SavedSelection | null {
  const match = /^(\d+)-(\d+)@(\d+)$/.exec(text ?? '');
  if (!match) return null;
  const [start, end, version] = match.slice(1).map(Number);
  return start < end ? { start, end, version } : null;
}

/** Where the `occurrence`-th match of `passage` sits in `body` (counting overlapping ones, as the server does). */
export function selectionIn(
  body: string,
  passage: string,
  occurrence: number,
  version: number,
): SavedSelection | null {
  let at = -1;
  for (let n = 0; n < occurrence; n += 1) {
    at = body.indexOf(passage, at + 1);
    if (at < 0) return null;
  }
  return { start: at, end: at + passage.length, version };
}

/** The words a saved selection covers in `body`, as `POST /api/citations` takes them. */
export function passageOf(body: string, selection: SavedSelection) {
  const passage = body.slice(selection.start, selection.end);
  return { passage, occurrence: occurrencesUpTo(body, passage, selection.start) };
}

export interface WriteReturn {
  scene?: string;
  /** Open that source's marker card. */
  ref?: string;
  /** With `ref`: prepare a beat that paraphrases that source's quote in the scene (References' Paraphrase with AI). */
  paraphrase?: boolean;
  sel?: string;
  /** The source to cite at `sel`. */
  cite?: string;
  title?: string;
}

const enc = encodeURIComponent;

/**
 * Write at a scene, carrying the detour's answer or a paraphrase request. `shell/links.ts`'s `writeLink` has no
 * `cite`/`title`/`paraphrase`/`sel`, so this adds them to its output with the same encoding (`parseDeepLink` reads
 * them back; `citeReturn.test.ts`).
 */
export function writeReturnLink(bookId: string, target: WriteReturn): string {
  const [path, hash = ''] = writeLink(bookId, { scene: target.scene, ref: target.ref }).split('#');
  const pairs = [
    hash,
    ...(
      [
        ['cite', target.cite],
        ['title', target.title],
        ['paraphrase', target.paraphrase ? '1' : undefined],
      ] as const
    ).flatMap(([key, value]) => (value === undefined ? [] : [`${key}=${enc(value)}`])),
  ].filter((pair) => pair !== '');
  const query = target.sel === undefined ? '' : `?sel=${enc(target.sel)}`;
  return path + query + (pairs.length === 0 ? '' : `#${pairs.join('&')}`);
}

/**
 * Where References goes after the detour: back to Write at the scene `returnTo` names, with the saved source to cite
 * at `sel` when there is one. Null when `returnTo` is not a Write link of `bookId` (nothing else is followed).
 */
export function returnLink(
  bookId: string,
  returnTo: string,
  answer: { sel?: string; cite?: string; title?: string } = {},
): string | null {
  const target = returnTarget(returnTo);
  if (target?.bookId !== bookId) return null;
  return answer.cite === undefined
    ? writeReturnLink(bookId, { scene: target.scene })
    : writeReturnLink(bookId, { scene: target.scene, ...answer });
}

/** The Write scene a `return` path points at, for "Will return to Write · Chapter n · Scene n". */
export function returnTarget(returnTo: string): { bookId: string; scene?: string } | null {
  const hashAt = returnTo.indexOf('#');
  const beforeHash = hashAt < 0 ? returnTo : returnTo.slice(0, hashAt);
  const queryAt = beforeHash.indexOf('?');
  const parsed = parseDeepLink(
    queryAt < 0 ? beforeHash : beforeHash.slice(0, queryAt),
    queryAt < 0 ? '' : beforeHash.slice(queryAt),
    hashAt < 0 ? '' : returnTo.slice(hashAt),
  );
  if (!parsed.ok || parsed.link.screen !== 'write' || parsed.link.bookId === undefined) return null;
  return { bookId: parsed.link.bookId, scene: parsed.link.scene };
}
