import { displayVersion } from '~/session/displayVersion';

/** `https://codex.example.com · Codex 1.0.0`; the origin alone while the version is unknown (not reached, or loading). */
export function serverLine(origin: string, version: string | null): string {
  return version === null || version.trim() === ''
    ? origin
    : `${origin} · Codex ${displayVersion(version)}`;
}
