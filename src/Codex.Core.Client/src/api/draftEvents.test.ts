import { describe, expect, it } from 'vitest';
import { draftEvents, type DraftStreamEvent } from './write';

async function* chunks(...parts: string[]) {
  for (const part of parts) yield part;
}

const collect = async (source: AsyncIterable<string>) => {
  const events: DraftStreamEvent[] = [];
  for await (const event of draftEvents(source)) events.push(event);
  return events;
};

describe('draftEvents', () => {
  it('reads one event per line, whole or split across chunks', async () => {
    const events = await collect(
      chunks('{"text":"He', 'llo "}\n{"text":"wor', 'ld"}\n{"final":', '{}}\n'),
    );

    expect(events).toEqual([{ text: 'Hello ' }, { text: 'world' }, { final: {} }]);
  });

  it('reads several lines from one chunk and a last line without a newline', async () => {
    const events = await collect(chunks('{"text":"a"}\n\n{"text":"b"}\n{"text":"c"}'));

    expect(events.map((e) => e.text)).toEqual(['a', 'b', 'c']);
  });
});
