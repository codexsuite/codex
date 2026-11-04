import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { BookEvent } from '@codex/core/api/events';

// The server side is Codex.WebApi/Realtime/BookHub.cs: JoinBook(bookId) starts a book's events, delivered as
// "event" messages carrying the BookEvent JSON (api/events.ts).
export const BOOK_HUB_PATH = '/hubs/book';
const EVENT_METHOD = 'event';

// The slice of HubConnection this module uses, so a test can pass a fake.
export type RealtimeConnection = Pick<
  HubConnection,
  'start' | 'stop' | 'invoke' | 'on' | 'onreconnected'
>;

// Seconds-scale back-off, used both for the first connect (SignalR does not retry that) and, through
// withAutomaticReconnect, for a dropped connection. The last delay repeats.
export const RETRY_DELAYS_MS = [0, 2000, 5000, 10000, 30000];

export function createBookConnection(): RealtimeConnection {
  return new HubConnectionBuilder()
    .withUrl(BOOK_HUB_PATH)
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
        RETRY_DELAYS_MS[Math.min(previousRetryCount, RETRY_DELAYS_MS.length - 1)],
    })
    .configureLogging(LogLevel.None)
    .build();
}

export interface SubscribeOptions {
  connect?: () => RealtimeConnection;
  retryDelaysMs?: readonly number[];
}

function isBookEvent(value: unknown): value is BookEvent {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as { type?: unknown }).type === 'string' &&
    typeof (value as { bookId?: unknown }).bookId === 'string'
  );
}

// Connects to the hub, joins the book and calls onEvent for each of its events. A connection that cannot be made
// is retried with back-off; after SignalR reconnects a dropped connection the book is joined again (the server
// forgets the groups of a dead connection). Events missed while offline are not replayed.
// The returned function stops everything: no more events, no more retries, the connection closed.
export function subscribeToBook(
  bookId: string,
  onEvent: (event: BookEvent) => void,
  { connect = createBookConnection, retryDelaysMs = RETRY_DELAYS_MS }: SubscribeOptions = {},
): () => void {
  const connection = connect();
  const wanted = bookId.toLowerCase();
  let stopped = false;
  let retry: ReturnType<typeof setTimeout> | undefined;

  const join = async () => {
    try {
      await connection.invoke('JoinBook', bookId);
    } catch {
      // An unknown book: the workspace shows its own error state, and there is nothing to retry.
    }
  };

  connection.on(EVENT_METHOD, (event: unknown) => {
    if (!stopped && isBookEvent(event) && event.bookId.toLowerCase() === wanted) onEvent(event);
  });
  connection.onreconnected(() => {
    if (!stopped) void join();
  });

  const open = async (attempt: number) => {
    try {
      await connection.start();
    } catch {
      if (stopped) return;
      const delay = retryDelaysMs[Math.min(attempt, retryDelaysMs.length - 1)];
      retry = setTimeout(() => void open(attempt + 1), delay);
      return;
    }
    if (stopped) return;
    await join();
  };
  void open(0);

  return () => {
    stopped = true;
    clearTimeout(retry);
    void connection.stop();
  };
}
