import { configurePlatform } from '@codex/core/platform';
import { webPlatform } from '../webPlatform';

// Vitest setup (vite.config.ts): the SPA's tests run on the browser platform, as the app does.
configurePlatform(webPlatform);
