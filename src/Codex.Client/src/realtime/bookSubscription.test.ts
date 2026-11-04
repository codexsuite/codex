import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BookEvent } from '@codex/core/api/events';
import { subscribeToBook, type RealtimeConnection } from './bookSubscription';

const book = '0199a000-0000-7000-8000-000000000012';
const structure: BookEvent = { type: 'structureChanged', bookId: book, kind: 'scene' };

// A hub connection that does what the test tells it to.
function fakeConnection(startResults: ('ok' | 'fail')[] = ['ok']) {
  const handlers: Record<string, (event: unknown) => void> = {};
  let onReconnected: () => void = () => undefined;
  const connection = {
    start: vi.fn(async () => {
      if (startResults.shift() === 'fail') throw new Error('offline');
    }),
    stop: vi.fn(async () => undefined),
    invoke: vi.fn(async () => undefined),
    on: vi.fn((method: string, handler: (event: unknown) => void) => {
      handlers[method] = handler;
    }),
    onreconnected: vi.fn((handler: () => void) => {
      onReconnected = handler;
    }),
  };
  return {
    connection: connection as unknown as RealtimeConnection,
    spies: connection,
    deliver: (event: unknown) => handlers['event']?.(event),
    reconnect: () => onReconnected(),
  };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('subscribeToBook', () => {
  it('joins the book once connected and passes its events on', async () => {
    const hub = fakeConnection();
    const received: BookEvent[] = [];

    subscribeToBook(book, (event) => received.push(event), { connect: () => hub.connection });
    await vi.advanceTimersByTimeAsync(0);
    hub.deliver(structure);

    expect(hub.spies.invoke).toHaveBeenCalledExactlyOnceWith('JoinBook', book);
    expect(received).toEqual([structure]);
  });

  it('ignores events of another book and anything that is not an event', async () => {
    const hub = fakeConnection();
    const received: BookEvent[] = [];
    subscribeToBook(book, (event) => received.push(event), { connect: () => hub.connection });
    await vi.advanceTimersByTimeAsync(0);

    hub.deliver({ ...structure, bookId: '0199a000-0000-7000-8000-000000000099' });
    hub.deliver('hello');
    hub.deliver(null);

    expect(received).toEqual([]);
  });

  it('joins the book again after SignalR reconnects', async () => {
    const hub = fakeConnection();
    subscribeToBook(book, () => undefined, { connect: () => hub.connection });
    await vi.advanceTimersByTimeAsync(0);

    hub.reconnect();

    expect(hub.spies.invoke).toHaveBeenCalledTimes(2);
    expect(hub.spies.invoke).toHaveBeenLastCalledWith('JoinBook', book);
  });

  it('retries a first connection that fails, with back-off, and joins when it succeeds', async () => {
    const hub = fakeConnection(['fail', 'fail', 'ok']);

    subscribeToBook(book, () => undefined, {
      connect: () => hub.connection,
      retryDelaysMs: [1000, 5000],
    });
    await vi.advanceTimersByTimeAsync(0);
    expect(hub.spies.start).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(999);
    expect(hub.spies.start).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(hub.spies.start).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(5000);

    expect(hub.spies.start).toHaveBeenCalledTimes(3);
    expect(hub.spies.invoke).toHaveBeenCalledExactlyOnceWith('JoinBook', book);
  });

  it('unsubscribing closes the connection, drops later events and cancels a pending retry', async () => {
    const failing = fakeConnection(['fail', 'ok']);
    const received: BookEvent[] = [];
    const stop = subscribeToBook(book, (event) => received.push(event), {
      connect: () => failing.connection,
      retryDelaysMs: [1000],
    });
    await vi.advanceTimersByTimeAsync(0);

    stop();
    await vi.advanceTimersByTimeAsync(5000);
    failing.deliver(structure);

    expect(failing.spies.start).toHaveBeenCalledTimes(1);
    expect(failing.spies.stop).toHaveBeenCalledTimes(1);
    expect(failing.spies.invoke).not.toHaveBeenCalled();
    expect(received).toEqual([]);
  });

  it('does not join a connection that was unsubscribed while it was starting', async () => {
    const hub = fakeConnection();
    const stop = subscribeToBook(book, () => undefined, { connect: () => hub.connection });

    stop();
    await vi.advanceTimersByTimeAsync(0);

    expect(hub.spies.invoke).not.toHaveBeenCalled();
  });

  it('keeps going when joining fails (unknown book)', async () => {
    const hub = fakeConnection();
    hub.spies.invoke.mockRejectedValueOnce(new Error('{"error":"nope","code":"not_found"}'));
    const received: BookEvent[] = [];

    subscribeToBook(book, (event) => received.push(event), { connect: () => hub.connection });
    await vi.advanceTimersByTimeAsync(0);
    hub.deliver(structure);

    expect(received).toEqual([structure]);
  });
});
