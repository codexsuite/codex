import { randomUUID } from 'expo-crypto';

// Hermes has no crypto.randomUUID; @codex/core's client calls it once at load time to name this app session.
const g = globalThis as { crypto?: { randomUUID?: () => string } };
g.crypto ??= {};
g.crypto.randomUUID ??= randomUUID;
