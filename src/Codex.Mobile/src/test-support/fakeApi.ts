// The network at fetch itself (the core's real API client runs): a table of `METHOD /path` to a response. Install one
// per test with `installFakeApi`; an unlisted request answers 404 `not_found` and is recorded in `calls`, so a spec can
// assert what the screen asked for (or that it did not ask).

export interface FakeRequest {
  method: string;
  /** The path without its query string. */
  path: string;
  query: URLSearchParams;
  /** The parsed JSON body, if the request had one. */
  body: unknown;
}

export interface FakeResult {
  status: number;
  body?: unknown;
}

/** A body (200), a `{ status, body }`, or a function of the request returning either. */
export type FakeHandler = unknown | ((request: FakeRequest) => unknown);

export interface FakeApi {
  readonly calls: FakeRequest[];
  /** Calls matching `METHOD /path`, e.g. `api.callsTo('PATCH /api/scenes/s1')`. */
  callsTo(route: string): FakeRequest[];
  /** Replaces or adds a route for the rest of the test. */
  respond(route: string, handler: FakeHandler): void;
}

export const FAKE_ORIGIN = 'https://codex.test';

const isResult = (value: unknown): value is FakeResult =>
  typeof value === 'object' &&
  value !== null &&
  'status' in value &&
  typeof (value as { status: unknown }).status === 'number';

export function installFakeApi(routes: Record<string, FakeHandler> = {}): FakeApi {
  const table = new Map(Object.entries(routes));
  const calls: FakeRequest[] = [];

  const fetchStub = jest.fn(async (input: unknown, init?: { method?: string; body?: unknown }) => {
    const url = new URL(String(input));
    const request: FakeRequest = {
      method: (init?.method ?? 'GET').toUpperCase(),
      path: url.pathname,
      query: url.searchParams,
      body: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined,
    };
    calls.push(request);
    const handler = table.get(`${request.method} ${request.path}`);
    const made =
      typeof handler === 'function' ? (handler as (r: FakeRequest) => unknown)(request) : handler;
    const result: FakeResult =
      handler === undefined
        ? {
            status: 404,
            body: { error: `No fake for ${request.method} ${request.path}`, code: 'not_found' },
          }
        : isResult(made)
          ? made
          : { status: made === undefined ? 204 : 200, body: made };
    return new Response(result.body === undefined ? null : JSON.stringify(result.body), {
      status: result.status,
      headers: { 'Content-Type': 'application/json' },
    });
  });
  globalThis.fetch = fetchStub as unknown as typeof fetch;

  return {
    calls,
    callsTo: (route) => calls.filter((call) => `${call.method} ${call.path}` === route),
    respond: (route, handler) => void table.set(route, handler),
  };
}
