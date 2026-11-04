/**
 * A new UUID (v4) for a beat the page creates. `crypto.randomUUID` exists only in secure contexts, and the app's server
 * can be plain http on a LAN, so this builds one from `getRandomValues`, which has no such limit.
 */
export function newId(random: (bytes: Uint8Array) => Uint8Array = fill): string {
  const bytes = random(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

const fill = (bytes: Uint8Array): Uint8Array => crypto.getRandomValues(bytes);
