import { platform } from '../platform';
import { isApiError, type ApiErrorCode } from './apiError';

// Single fetch wrapper for all API calls. It parses the API's { error, code } body on
// failure; `code` is undefined when the body was not an ApiError.
export class ApiRequestError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: ApiErrorCode,
  ) {
    super(message);
  }
}

// `signal` cancels the request (react-query passes one to every query function).
export interface RequestOptions {
  signal?: AbortSignal;
}

// Any 401 means the session is gone (signed out, expired, disabled, password reset elsewhere): the platform takes the
// user to sign in (web: the sign-in page, which brings them back here afterwards). Not for the sign-in call itself,
// whose 401 is a wrong password. Replaceable so a unit test can watch it.
export const session = {
  onUnauthorized: () => platform().onUnauthorized(),
};

// The sign-in calls, whose 401 is a wrong password rather than a lost session: the web's cookie sign-in and the
// mobile app's device sign-in.
const SIGN_IN_PATHS = new Set(['/api/auth/login', '/api/auth/device-sessions']);

function signInLost(method: string, path: string, status: number) {
  if (status === 401 && !(method === 'POST' && SIGN_IN_PATHS.has(path))) session.onUnauthorized();
}

// The platform says where the server is and how this app proves who it is (web: same origin, the cookie travels by
// itself; mobile: the chosen server and a bearer token).
function send(
  method: string,
  path: string,
  init: { headers: Record<string, string> } & RequestInit,
) {
  const host = platform();
  return fetch(host.apiOrigin() + path, {
    ...init,
    method,
    headers: { ...host.authHeaders(), [CLIENT_HEADER]: clientId, ...init.headers },
  });
}

// Names this page load to the server (header below), which stamps it on the book events a request causes, so the page
// can tell its own writes from someone else's. Per page load, never stored: a reload is a new client.
export const CLIENT_HEADER = 'X-Codex-Client';
export const clientId = crypto.randomUUID();

async function request<T>(
  method: string,
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<T> {
  const isForm = body instanceof FormData;
  const response = await send(method, path, {
    headers: body !== undefined && !isForm ? { 'Content-Type': 'application/json' } : {},
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    signal: options.signal,
  });
  if (!response.ok) {
    signInLost(method, path, response.status);
    const { message, code } = await errorDetails(response);
    throw new ApiRequestError(message, response.status, code);
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

async function errorDetails(response: Response): Promise<{ message: string; code?: ApiErrorCode }> {
  const fallback = `Request failed (${response.status})`;
  try {
    const parsed: unknown = await response.json();
    return isApiError(parsed)
      ? { message: parsed.error, code: parsed.code }
      : { message: fallback };
  } catch {
    return { message: fallback };
  }
}

// POSTs JSON and, once the response is 2xx, yields its body as decoded text chunks as they arrive (UTF-8 split
// across network chunks is handled). A non-2xx response throws ApiRequestError before anything is yielded.
// What the chunks mean (plain text, NDJSON events…) is the endpoint's contract, not this wrapper's. Aborting
// `signal` stops the stream: the pending iteration rejects with an AbortError and the caller keeps what it had.
async function stream(
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<AsyncIterable<string>> {
  const response = await send('POST', path, {
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: options.signal,
  });
  if (!response.ok) {
    signInLost('POST', path, response.status);
    const { message, code } = await errorDetails(response);
    throw new ApiRequestError(message, response.status, code);
  }
  return decodeChunks(response.body);
}

async function* decodeChunks(body: ReadableStream<Uint8Array> | null): AsyncGenerator<string> {
  if (body === null) return;
  const reader = body.getReader();
  const decoder = new TextDecoder();
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      const text = decoder.decode(value, { stream: true });
      if (text !== '') yield text;
    }
    const tail = decoder.decode();
    if (tail !== '') yield tail;
  } finally {
    reader.releaseLock();
  }
}

// Writes send JSON; `upload` sends multipart form data (files) as the browser encodes it.
export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>('GET', path, undefined, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>('POST', path, body, options),
  put: <T>(path: string, body: unknown, options?: RequestOptions) =>
    request<T>('PUT', path, body, options),
  patch: <T>(path: string, body: unknown, options?: RequestOptions) =>
    request<T>('PATCH', path, body, options),
  del: <T>(path: string, options?: RequestOptions) =>
    request<T>('DELETE', path, undefined, options),
  upload: <T>(path: string, form: FormData, options?: RequestOptions) =>
    request<T>('POST', path, form, options),
  stream,
};
