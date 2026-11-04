import { describe, expect, it } from 'vitest';
import {
  EDITOR_BRIDGE_VERSION,
  decodeHostMessage,
  decodePageMessage,
  editorCommands,
  encodeHostMessage,
  encodePageMessage,
  type BridgeScene,
  type HostMessage,
  type PageMessage,
} from './editorBridge';

const scene: BridgeScene = {
  id: 'scene-1',
  number: '1.1',
  title: 'An Empty Seat',
  labels: ['Draft', 'Needs check'],
  words: 1200,
  summary: 'The court waits.',
  chips: [{ id: 'kb-1', name: 'Ana', category: 'person' }],
  beats: [{ id: 'beat-1', title: '', body: 'She enters.', targetWordCount: 300, hidden: false }],
  additions: [{ id: 'add-1', entryId: 'kb-1', entryName: 'Ana', body: 'Wears grey.' }],
  prose: 'Hello @[Ana](kb:kb-1).\n\n::beat{id="beat-1"}\n',
  version: 4,
};

const hostMessages: HostMessage[] = [
  {
    type: 'load',
    v: EDITOR_BRIDGE_VERSION,
    theme: 'dark',
    density: 'touch',
    scenes: [scene, { ...scene, id: 'scene-2', beats: [], additions: [], prose: '', version: 0 }],
    entries: [{ id: 'kb-1', name: 'Ana', category: 'person', aliases: ['A.', 'The Queen'] }],
    scrollToSceneId: 'scene-2',
    readOnly: false,
  },
  {
    type: 'load',
    v: EDITOR_BRIDGE_VERSION,
    theme: 'light',
    density: 'compact',
    scenes: [],
    entries: [],
    scrollToSceneId: null,
    readOnly: true,
  },
  ...editorCommands.map((command): HostMessage => ({ type: 'command', command })),
  { type: 'setScene', scene: { ...scene, prose: 'New text.\n', version: 5 } },
  {
    type: 'sceneMeta',
    sceneId: 'scene-1',
    title: 'Renamed',
    labels: ['Final'],
    words: 10,
    summary: 'New summary.',
    chips: [{ id: 'kb-2', name: 'Bo', category: 'person' }],
  },
  { type: 'scrollTo', sceneId: 'scene-2' },
];

const pageMessages: PageMessage[] = [
  { type: 'ready', v: EDITOR_BRIDGE_VERSION },
  { type: 'change', sceneId: 'scene-1', markdown: 'Edited.\n' },
  { type: 'focus', sceneId: 'scene-1' },
  { type: 'blur', sceneId: 'scene-1' },
  { type: 'openScene', sceneId: 'scene-1' },
  {
    type: 'action',
    sceneId: 'scene-1',
    action: 'beat',
    beatId: 'beat-9',
    body: 'Text\n\n::beat{id="beat-9"}\n',
  },
  {
    type: 'action',
    sceneId: 'scene-1',
    action: 'newScene',
    headBody: 'Head\n',
    tailBody: 'Tail\n',
  },
  { type: 'state', bold: true, italic: false, quote: false, canUndo: true, canRedo: false },
  { type: 'contentHeight', height: 1432.5 },
  { type: 'error', message: 'The editor failed to start.' },
];

describe('host messages', () => {
  it.each(hostMessages)('decode(encode($type)) is the same message', (message) => {
    expect(decodeHostMessage(encodeHostMessage(message))).toEqual({ ok: true, message });
  });

  it.each([
    ['not a string', { type: 'scrollTo' }, 'a message is a JSON string'],
    ['not JSON', '{nope', 'not JSON'],
    ['an array', '[]', 'not an object'],
    ['an unknown type', '{"type":"explode"}', 'unknown message type "explode"'],
    ['a page message', '{"type":"ready","v":1}', 'unknown message type "ready"'],
    ['a prototype key as type', '{"type":"constructor"}', 'unknown message type "constructor"'],
    ['an unknown command', '{"type":"command","command":"rm"}', 'command: unknown command'],
    ['a missing scene id', '{"type":"scrollTo"}', 'scrollTo: sceneId must be a string'],
    [
      'a setScene without a scene',
      '{"type":"setScene","sceneId":"a","prose":"","version":1}',
      'setScene: scene must be a scene',
    ],
  ])('rejects %s', (_name, raw, reason) => {
    expect(decodeHostMessage(raw)).toEqual({ ok: false, reason });
  });

  it('rejects a load with an unknown theme', () => {
    const raw = JSON.stringify({ ...hostMessages[0], theme: 'sepia' });
    expect(decodeHostMessage(raw)).toEqual({
      ok: false,
      reason: 'load: theme must be light or dark',
    });
  });

  it('rejects a load whose scene is missing a field', () => {
    const broken: Record<string, unknown> = { ...scene };
    delete broken.prose;
    const raw = JSON.stringify({ ...hostMessages[0], scenes: [broken] });
    expect(decodeHostMessage(raw)).toEqual({
      ok: false,
      reason: 'load: scenes must be a list of scenes',
    });
  });

  it('rejects a load whose beat has a string target', () => {
    const raw = JSON.stringify({
      ...hostMessages[0],
      scenes: [
        {
          ...scene,
          beats: [{ id: 'b', title: '', body: '', targetWordCount: '300', hidden: false }],
        },
      ],
    });
    expect(decodeHostMessage(raw).ok).toBe(false);
  });

  it('rejects a load whose beat has no hidden flag', () => {
    const raw = JSON.stringify({
      ...hostMessages[0],
      scenes: [{ ...scene, beats: [{ id: 'b', title: '', body: '', targetWordCount: 300 }] }],
    });
    expect(decodeHostMessage(raw).ok).toBe(false);
  });
});

describe('page messages', () => {
  it.each(pageMessages)('decode(encode($type)) is the same message', (message) => {
    expect(decodePageMessage(encodePageMessage(message))).toEqual({ ok: true, message });
  });

  it.each([
    ['an unknown type', '{"type":"load"}', 'unknown message type "load"'],
    ['a missing markdown', '{"type":"change","sceneId":"a"}', 'change: markdown must be a string'],
    [
      'an unknown action',
      '{"type":"action","action":"delete","sceneId":"a"}',
      'action: unknown action',
    ],
    [
      'a negative height',
      '{"type":"contentHeight","height":-1}',
      'contentHeight: height must be a non-negative number',
    ],
    [
      'NaN as a height',
      '{"type":"contentHeight","height":null}',
      'contentHeight: height must be a non-negative number',
    ],
    [
      'a partial state',
      '{"type":"state","bold":true}',
      'state: bold, italic, quote, canUndo and canRedo must be booleans',
    ],
  ])('rejects %s', (_name, raw, reason) => {
    expect(decodePageMessage(raw)).toEqual({ ok: false, reason });
  });

  it('ignores fields it does not know', () => {
    const raw = '{"type":"focus","sceneId":"a","extra":1}';
    expect(decodePageMessage(raw)).toEqual({ ok: true, message: { type: 'focus', sceneId: 'a' } });
  });
});
