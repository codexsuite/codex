import { ApiRequestError, api } from './client';

/** `GET /api/health` (C#: `HealthEndpoints`): anonymous. `version` is the build's informational version (`1.0.0+<sha>`). */
export interface Health {
  status: string;
  version: string;
}

export const healthKey = (origin: string | null) => ['health', origin] as const;

/**
 * Asks a server how it is. With no `origin` it asks the platform's server through the shared client. With an `origin`
 * (`https://codex.example.com`, no trailing slash) it asks that server directly, with no session headers: sign-in
 * checks the address before a session exists.
 */
export async function getHealth(
  options: { origin?: string; signal?: AbortSignal } = {},
): Promise<Health> {
  const { origin, signal } = options;
  if (origin === undefined) return api.get<Health>('/api/health', { signal });
  const response = await fetch(`${origin}/api/health`, { signal });
  if (!response.ok) {
    throw new ApiRequestError(`Request failed (${response.status})`, response.status);
  }
  return response.json();
}
