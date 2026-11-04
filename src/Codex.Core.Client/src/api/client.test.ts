import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubFetch } from '../test-support/stubFetch';
import { configurePlatform, memoryStore, platform, type Platform } from '../platform';
import { api, ApiRequestError, CLIENT_HEADER, clientId } from './client';

describe('api client', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('serializes the body and sets the content type on writes', async () => {
    const fetchMock = stubFetch(201, {});

    await api.post('/api/books', { title: 'X' });

    const [, init] = fetchMock.mock.calls[0];
    expect(init.body).toBe('{"title":"X"}');
    expect(init.headers['Content-Type']).toBe('application/json');
  });

  it('names this page load on every request, reads included', async () => {
    const read = stubFetch(200, {});
    await api.get('/api/books');
    const write = stubFetch(204);
    await api.put('/api/x', {});

    expect(clientId).toMatch(/^[A-Za-z0-9-]{1,64}$/);
    expect(read.mock.calls[0][1].headers[CLIENT_HEADER]).toBe(clientId);
    expect(write.mock.calls[0][1].headers[CLIENT_HEADER]).toBe(clientId);
  });

  it('surfaces the API error body message on failure', async () => {
    stubFetch(404, { error: 'Book 7 not found.', code: 'not_found' });

    await expect(api.get('/api/books/7')).rejects.toThrow('Book 7 not found.');
  });

  it('falls back to a generic message when the error body is not JSON', async () => {
    stubFetch(500);

    await expect(api.get('/api/books')).rejects.toThrow('Request failed (500)');
  });

  it('sends PATCH bodies as JSON', async () => {
    const fetchMock = stubFetch(200, {});

    await api.patch('/api/scenes/1', { title: 'Y' });

    const [, init] = fetchMock.mock.calls[0];
    expect(init.method).toBe('PATCH');
    expect(init.body).toBe('{"title":"Y"}');
  });

  it('uploads form data without a JSON content type', async () => {
    const fetchMock = stubFetch(201, {});
    const form = new FormData();
    form.append('file', new Blob(['x']), 'x.txt');

    await api.upload('/api/assets', form);

    const [, init] = fetchMock.mock.calls[0];
    expect(init.body).toBe(form);
    expect(init.headers['Content-Type']).toBeUndefined();
  });

  it('carries the status and the ApiError code', async () => {
    stubFetch(409, { error: 'Stale version.', code: 'conflict' });

    const error = await api.put('/api/scenes/1/prose', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).status).toBe(409);
    expect((error as ApiRequestError).code).toBe('conflict');
  });

  it('sends requests to the platform origin with its auth headers', async () => {
    const before = platform();
    const mobile: Platform = {
      ...before,
      persistentStore: memoryStore,
      apiOrigin: () => 'https://codex.example.com',
      authHeaders: () => ({ Authorization: 'Bearer cdx_dev_x' }),
    };
    configurePlatform(mobile);
    try {
      const read = stubFetch(200, {});
      await api.get('/api/library');
      const stream = stubFetch(200, '');
      await api.stream('/api/x', {});

      expect(read.mock.calls[0][0]).toBe('https://codex.example.com/api/library');
      expect(read.mock.calls[0][1].headers.Authorization).toBe('Bearer cdx_dev_x');
      expect(read.mock.calls[0][1].headers[CLIENT_HEADER]).toBe(clientId);
      expect(stream.mock.calls[0][0]).toBe('https://codex.example.com/api/x');
      expect(stream.mock.calls[0][1].headers.Authorization).toBe('Bearer cdx_dev_x');
    } finally {
      configurePlatform(before);
    }
  });

  it('returns undefined for 204 responses', async () => {
    stubFetch(204);

    await expect(api.del('/api/books/1')).resolves.toBeUndefined();
  });

  it('streams a 2xx body as decoded text chunks', async () => {
    const bytes = new TextEncoder().encode('Café — done');
    const parts = [bytes.slice(0, 4), bytes.slice(4, 8), bytes.slice(8)]; // splits é and — across chunks
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        new ReadableStream<Uint8Array>({
          start(controller) {
            parts.forEach((part) => controller.enqueue(part));
            controller.close();
          },
        }),
        { status: 200 },
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    const chunks: string[] = [];
    for await (const chunk of await api.stream(
      '/api/beats/1/draft',
      { model: 'm' },
      { signal: controller.signal },
    )) {
      chunks.push(chunk);
    }

    expect(chunks.join('')).toBe('Café — done');
    expect(chunks.length).toBeGreaterThan(1);
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe('/api/beats/1/draft');
    expect(init.method).toBe('POST');
    expect(init.body).toBe('{"model":"m"}');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(init.signal).toBe(controller.signal);
  });

  it('throws the ApiError before streaming when the response is not 2xx', async () => {
    stubFetch(503, { error: 'AI drafting is not configured.', code: 'not_configured' });

    const error = await api.stream('/api/beats/1/draft', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiRequestError);
    expect((error as ApiRequestError).status).toBe(503);
    expect((error as ApiRequestError).code).toBe('not_configured');
  });
});
