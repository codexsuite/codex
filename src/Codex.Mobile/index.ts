// Polyfills first: @codex/core reads crypto.randomUUID when it loads (its client id).
import './src/polyfills';
import 'expo-router/entry';
