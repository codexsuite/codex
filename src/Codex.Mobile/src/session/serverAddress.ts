// The server address the user types on the sign-in screen (#35). Pure: no Expo imports, so vitest loads it.

export type ServerAddress = { ok: true; origin: string } | { ok: false; error: string };

const SCHEME = /^([a-z][a-z0-9+.-]*):\/\//i;

/**
 * `codex.example.com` -> `https://codex.example.com`. `http://` stays when typed (dev servers on a LAN or the Android
 * emulator's `10.0.2.2`). Anything after the host (path, query, hash, trailing slash) is dropped.
 */
export function normalizeServerAddress(input: string): ServerAddress {
  const typed = input.trim();
  if (typed === '') return { ok: false, error: 'Enter the address of your Codex server.' };

  const scheme = SCHEME.exec(typed);
  if (scheme && !['http', 'https'].includes(scheme[1].toLowerCase())) {
    return { ok: false, error: 'The address must start with http:// or https://.' };
  }

  let url: URL;
  try {
    url = new URL(scheme ? typed : `https://${typed}`);
  } catch {
    return { ok: false, error: "That doesn't look like a server address." };
  }

  if (url.username !== '' || url.password !== '') {
    return { ok: false, error: 'Leave the user name and password out of the address.' };
  }
  if (url.hostname === '') return { ok: false, error: "That doesn't look like a server address." };

  return { ok: true, origin: `${url.protocol}//${url.host}` };
}

/** `https://codex.example.com:8443` -> `codex.example.com:8443`, for "Couldn't reach …" and the Library's server line. */
export function serverHost(origin: string): string {
  return origin.replace(SCHEME, '');
}
