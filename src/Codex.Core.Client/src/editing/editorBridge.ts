// The message protocol between the mobile app (React Native) and the editor page it loads into a WebView (#35).
// The app owns every save and every API call; the page edits text and reports what changed. Messages travel as JSON
// strings. No DOM and no React here, so the app, the page and vitest all import the same file.
//
// Both directions are validated on decode: a message that does not match is rejected with a reason, never half
// trusted. Add a field by making it optional and keeping `EDITOR_BRIDGE_VERSION`; change the meaning of an existing
// field or message by raising the version (the page reports its version in `ready`, the app sends its own in `load`).

export const EDITOR_BRIDGE_VERSION = 1;

export type BridgeTheme = 'light' | 'dark';
export type BridgeDensity = 'compact' | 'comfortable' | 'touch';

/** A Knowledge base chip on a scene card. */
export interface BridgeChip {
  id: string;
  name: string;
  category: string;
}

/** A beat in a scene: the page draws its `::beat{id="…"}` directive from this. */
export interface BridgeBeat {
  id: string;
  title: string;
  body: string;
  targetWordCount: number | null;
  /** The beat is hidden (desktop `isHidden`): the page draws it as a one-line row and keeps its prose visible. */
  hidden: boolean;
}

/** A Knowledge base addition in a scene: the page draws its `::kb-addition{id="…"}` directive from this. */
export interface BridgeAddition {
  id: string;
  entryId: string;
  entryName: string;
  body: string;
}

/** One scene of the chapter, as the page shows it: its card above the prose. */
export interface BridgeScene {
  id: string;
  /** The display number, `3.2`. */
  number: string;
  title: string;
  labels: string[];
  words: number;
  summary: string;
  chips: BridgeChip[];
  beats: BridgeBeat[];
  additions: BridgeAddition[];
  /** Stored scene Markdown (`docs/prose-format.md`). */
  prose: string;
  /** The version the app last saved or loaded; the page never changes it. */
  version: number;
}

/** A Knowledge base entry the `@` picker offers. */
export interface BridgeEntry {
  id: string;
  name: string;
  category: string;
  aliases: string[];
}

export const editorCommands = [
  'bold',
  'italic',
  'quote',
  'slash',
  'mention',
  'beat',
  'newScene',
  'undo',
  'redo',
  'blur',
] as const;
export type EditorCommand = (typeof editorCommands)[number];

/**
 * What the page can ask the app to do for a scene. Only the page has the cursor, so it sends the finished text, exactly
 * what the desktop sends the server (`blockApi.insertBeat` / `splitScene`, Codex.Client `useBlockOps`); the app adds the
 * scene's saved `version` and calls the API. Before calling it the app flushes and awaits that scene's pending autosave (as
 * the desktop does), or the call 409s on a stale version.
 */
export const pageActionNames = ['beat', 'newScene'] as const;
export type PageAction =
  /** Insert a beat at the cursor: `beatId` is a new UUID the page made, `body` the scene Markdown with `::beat{id="beatId"}` inserted. */
  | { action: 'beat'; beatId: string; body: string }
  /** Split the scene at the cursor: the Markdown before and after it. */
  | { action: 'newScene'; headBody: string; tailBody: string };

// RN to page.
export type HostMessage =
  | {
      type: 'load';
      v: number;
      theme: BridgeTheme;
      density: BridgeDensity;
      /** The chapter's scenes in reading order. */
      scenes: BridgeScene[];
      entries: BridgeEntry[];
      /** Scroll here once drawn; null leaves the chapter at its top. */
      scrollToSceneId: string | null;
      readOnly: boolean;
    }
  | { type: 'command'; command: EditorCommand }
  /**
   * Replaces one scene wholesale (card, beats, additions, prose). Send it only when the page's text is NOT the truth:
   * another session changed the scene, a save conflicted and was reloaded, or a block operation the app performed
   * through the server (insert beat, split scene) rewrote the scene body. Never send it after the app's own autosave of
   * what the page reported: the editor replaces its content when the value differs, so text typed while the save was
   * in flight would be lost. Carries beats and additions so a just-inserted beat draws without a full `load`.
   */
  | { type: 'setScene'; scene: BridgeScene }
  /**
   * Updates a scene card's details (the scene sheet edits them over Write) without touching the prose or the cursor.
   */
  | {
      type: 'sceneMeta';
      sceneId: string;
      title: string;
      labels: string[];
      words: number;
      summary: string;
      chips: BridgeChip[];
    }
  | { type: 'scrollTo'; sceneId: string };

// Page to RN.
export type PageMessage =
  | { type: 'ready'; v: number }
  | { type: 'change'; sceneId: string; markdown: string }
  | { type: 'focus'; sceneId: string }
  | { type: 'blur'; sceneId: string }
  /** The reader tapped a scene card. */
  | { type: 'openScene'; sceneId: string }
  | ({ type: 'action'; sceneId: string } & PageAction)
  /** The editor's selection state, for the accessory bar (active buttons, undo and redo enabled). */
  | {
      type: 'state';
      bold: boolean;
      italic: boolean;
      quote: boolean;
      canUndo: boolean;
      canRedo: boolean;
    }
  | { type: 'contentHeight'; height: number }
  | { type: 'error'; message: string };

export type Decoded<T> = { ok: true; message: T } | { ok: false; reason: string };

export const encodeHostMessage = (message: HostMessage): string => JSON.stringify(message);
export const encodePageMessage = (message: PageMessage): string => JSON.stringify(message);

export const decodeHostMessage = (raw: unknown): Decoded<HostMessage> =>
  decode(raw, hostValidators);
export const decodePageMessage = (raw: unknown): Decoded<PageMessage> =>
  decode(raw, pageValidators);

type Fields = Record<string, unknown>;
type Validator<T> = (fields: Fields) => T | string;
type Validators<T extends { type: string }> = {
  [K in T['type']]: Validator<Extract<T, { type: K }>>;
};

function decode<T extends { type: string }>(raw: unknown, validators: Validators<T>): Decoded<T> {
  if (typeof raw !== 'string') return fail('a message is a JSON string');
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return fail('not JSON');
  }
  if (!isRecord(parsed)) return fail('not an object');
  const type = parsed.type;
  if (typeof type !== 'string' || !Object.hasOwn(validators, type)) {
    return fail(`unknown message type ${JSON.stringify(type)}`);
  }
  const result = (validators as Record<string, Validator<T>>)[type]!(parsed);
  return typeof result === 'string' ? fail(`${type}: ${result}`) : { ok: true, message: result };
}

const fail = (reason: string): { ok: false; reason: string } => ({ ok: false, reason });
const isRecord = (value: unknown): value is Fields =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

// Field readers: each returns the value, or undefined when it is missing or the wrong type.
const str = (value: unknown): string | undefined => (typeof value === 'string' ? value : undefined);
const num = (value: unknown): number | undefined =>
  typeof value === 'number' && Number.isFinite(value) ? value : undefined;
const bool = (value: unknown): boolean | undefined =>
  typeof value === 'boolean' ? value : undefined;

function oneOf<T extends string>(value: unknown, allowed: readonly T[]): T | undefined {
  return allowed.find((candidate) => candidate === value);
}

function list<T>(value: unknown, read: (item: unknown) => T | undefined): T[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const items: T[] = [];
  for (const item of value) {
    const read1 = read(item);
    if (read1 === undefined) return undefined;
    items.push(read1);
  }
  return items;
}

const readStrings = (value: unknown) => list(value, str);

function readChip(value: unknown): BridgeChip | undefined {
  if (!isRecord(value)) return undefined;
  const { id, name, category } = {
    id: str(value.id),
    name: str(value.name),
    category: str(value.category),
  };
  return id === undefined || name === undefined || category === undefined
    ? undefined
    : { id, name, category };
}

function readBeat(value: unknown): BridgeBeat | undefined {
  if (!isRecord(value)) return undefined;
  const id = str(value.id);
  const title = str(value.title);
  const body = str(value.body);
  const target = value.targetWordCount === null ? null : num(value.targetWordCount);
  const hidden = typeof value.hidden === 'boolean' ? value.hidden : undefined;
  return id === undefined ||
    title === undefined ||
    body === undefined ||
    target === undefined ||
    hidden === undefined
    ? undefined
    : { id, title, body, targetWordCount: target, hidden };
}

function readAddition(value: unknown): BridgeAddition | undefined {
  if (!isRecord(value)) return undefined;
  const id = str(value.id);
  const entryId = str(value.entryId);
  const entryName = str(value.entryName);
  const body = str(value.body);
  return id === undefined || entryId === undefined || entryName === undefined || body === undefined
    ? undefined
    : { id, entryId, entryName, body };
}

function readEntry(value: unknown): BridgeEntry | undefined {
  if (!isRecord(value)) return undefined;
  const id = str(value.id);
  const name = str(value.name);
  const category = str(value.category);
  const aliases = readStrings(value.aliases);
  return id === undefined || name === undefined || category === undefined || aliases === undefined
    ? undefined
    : { id, name, category, aliases };
}

function readScene(value: unknown): BridgeScene | undefined {
  if (!isRecord(value)) return undefined;
  const id = str(value.id);
  const number = str(value.number);
  const title = str(value.title);
  const labels = readStrings(value.labels);
  const words = num(value.words);
  const summary = str(value.summary);
  const chips = list(value.chips, readChip);
  const beats = list(value.beats, readBeat);
  const additions = list(value.additions, readAddition);
  const prose = str(value.prose);
  const version = num(value.version);
  if (
    id === undefined ||
    number === undefined ||
    title === undefined ||
    labels === undefined ||
    words === undefined ||
    summary === undefined ||
    chips === undefined ||
    beats === undefined ||
    additions === undefined ||
    prose === undefined ||
    version === undefined
  ) {
    return undefined;
  }
  return { id, number, title, labels, words, summary, chips, beats, additions, prose, version };
}

const sceneIdError = 'sceneId must be a string';

const bridgeThemes: readonly BridgeTheme[] = ['light', 'dark'];
const bridgeDensities: readonly BridgeDensity[] = ['compact', 'comfortable', 'touch'];

const hostValidators: Validators<HostMessage> = {
  load(f) {
    const v = num(f.v);
    const theme = oneOf(f.theme, bridgeThemes);
    const density = oneOf(f.density, bridgeDensities);
    const scenes = list(f.scenes, readScene);
    const entries = list(f.entries, readEntry);
    const scrollToSceneId = f.scrollToSceneId === null ? null : str(f.scrollToSceneId);
    const readOnly = bool(f.readOnly);
    if (v === undefined) return 'v must be a number';
    if (theme === undefined) return 'theme must be light or dark';
    if (density === undefined) return 'density must be compact, comfortable or touch';
    if (scenes === undefined) return 'scenes must be a list of scenes';
    if (entries === undefined) return 'entries must be a list of entries';
    if (scrollToSceneId === undefined) return 'scrollToSceneId must be a string or null';
    if (readOnly === undefined) return 'readOnly must be a boolean';
    return { type: 'load', v, theme, density, scenes, entries, scrollToSceneId, readOnly };
  },
  command(f) {
    const command = oneOf(f.command, editorCommands);
    return command === undefined ? 'unknown command' : { type: 'command', command };
  },
  setScene(f) {
    const scene = readScene(f.scene);
    return scene === undefined ? 'scene must be a scene' : { type: 'setScene', scene };
  },
  sceneMeta(f) {
    const sceneId = str(f.sceneId);
    const title = str(f.title);
    const labels = readStrings(f.labels);
    const words = num(f.words);
    const summary = str(f.summary);
    const chips = list(f.chips, readChip);
    if (sceneId === undefined) return sceneIdError;
    if (title === undefined) return 'title must be a string';
    if (labels === undefined) return 'labels must be a list of strings';
    if (words === undefined) return 'words must be a number';
    if (summary === undefined) return 'summary must be a string';
    if (chips === undefined) return 'chips must be a list of chips';
    return { type: 'sceneMeta', sceneId, title, labels, words, summary, chips };
  },
  scrollTo(f) {
    const sceneId = str(f.sceneId);
    return sceneId === undefined ? sceneIdError : { type: 'scrollTo', sceneId };
  },
};

const pageValidators: Validators<PageMessage> = {
  ready(f) {
    const v = num(f.v);
    return v === undefined ? 'v must be a number' : { type: 'ready', v };
  },
  change(f) {
    const sceneId = str(f.sceneId);
    const markdown = str(f.markdown);
    if (sceneId === undefined) return 'sceneId must be a string';
    if (markdown === undefined) return 'markdown must be a string';
    return { type: 'change', sceneId, markdown };
  },
  focus(f) {
    const sceneId = str(f.sceneId);
    return sceneId === undefined ? sceneIdError : { type: 'focus', sceneId };
  },
  blur(f) {
    const sceneId = str(f.sceneId);
    return sceneId === undefined ? sceneIdError : { type: 'blur', sceneId };
  },
  openScene(f) {
    const sceneId = str(f.sceneId);
    return sceneId === undefined ? sceneIdError : { type: 'openScene', sceneId };
  },
  action(f) {
    const sceneId = str(f.sceneId);
    if (sceneId === undefined) return sceneIdError;
    if (f.action === 'beat') {
      const beatId = str(f.beatId);
      const body = str(f.body);
      if (beatId === undefined) return 'beatId must be a string';
      if (body === undefined) return 'body must be a string';
      return { type: 'action', sceneId, action: 'beat', beatId, body };
    }
    if (f.action === 'newScene') {
      const headBody = str(f.headBody);
      const tailBody = str(f.tailBody);
      if (headBody === undefined) return 'headBody must be a string';
      if (tailBody === undefined) return 'tailBody must be a string';
      return { type: 'action', sceneId, action: 'newScene', headBody, tailBody };
    }
    return 'unknown action';
  },
  state(f) {
    const { bold, italic, quote, canUndo, canRedo } = {
      bold: bool(f.bold),
      italic: bool(f.italic),
      quote: bool(f.quote),
      canUndo: bool(f.canUndo),
      canRedo: bool(f.canRedo),
    };
    return bold === undefined ||
      italic === undefined ||
      quote === undefined ||
      canUndo === undefined ||
      canRedo === undefined
      ? 'bold, italic, quote, canUndo and canRedo must be booleans'
      : { type: 'state', bold, italic, quote, canUndo, canRedo };
  },
  contentHeight(f) {
    const height = num(f.height);
    return height === undefined || height < 0
      ? 'height must be a non-negative number'
      : { type: 'contentHeight', height };
  },
  error(f) {
    const message = str(f.message);
    return message === undefined ? 'message must be a string' : { type: 'error', message };
  },
};
