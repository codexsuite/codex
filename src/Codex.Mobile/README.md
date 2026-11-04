# Codex.Mobile

The Expo app for Codex issue #35. It is a thin client over `@codex/core` (`src/Codex.Core.Client`).

Stack: Expo SDK 58, expo-router, react-query, and device-session sign-in with a server address field. The screens follow "Mobile screen patterns" in `design/README.md`.

## Commands

Run from `src/Codex.Mobile`.

- `pnpm start` starts Metro and the Expo dev tools.
- `pnpm run typecheck`, `pnpm run lint`, `pnpm test`, `pnpm run test:screens` and `pnpm run format:check` are the CI checks.
- `pnpm test` is vitest for the pure modules (`src/**/*.test.ts`, plain Node). `pnpm run test:screens` is jest-expo with
  @testing-library/react-native for screens and primitives (`src/**/*.spec.{ts,tsx}`); the two patterns never overlap. Specs go
  in `src/`, never in `app/` (expo-router would treat them as routes). `src/test-support/` has the harness
  (`renderScreen`, `renderApp`, the fake API, fake data builders, the react-native-webview mock, the fake speech engine)
  and is for tests only; `@testing-library/react-native` v14 is async (`await render`, `await fireEvent.press`).
- `pnpm run bundle` runs the Metro export (`expo export --platform android`) that CI uses to prove the app bundles.

## Theme

`src/theme/tokens.generated.ts` is generated from `design/tokens.json` by `scripts/generate-theme.mjs`. It holds the light and dark colours, the type scale with the touch density overrides, weights, touch sizes, space and radius. Do not edit it by hand.

Rerun the generator whenever `design/tokens.json` changes:

```
node scripts/generate-theme.mjs
```

`src/theme/tokens.test.ts` compares the generated file with `tokens.json`, so CI fails if you forget. Screens read the theme through `useTheme()` from `src/theme`; the pure values live in `themes.ts`.

## Running against a dev server

The app signs in with a server address, so start the Codex server first (it listens on port 5688), then enter the address that matches where the app runs:

- Android emulator: `http://10.0.2.2:5688`
- iOS simulator: `http://localhost:5688`
- A phone on the same network: `http://<your computer's LAN IP>:5688`

1. Start the server: `dotnet run --project src/Codex.WebApi` (from the repo root). It binds `localhost` only, which is enough
   for the simulators; for a phone, bind every interface instead:
   `dotnet run --project src/Codex.WebApi --urls http://0.0.0.0:5688`, and let the port through the firewall.
2. Start the app: `pnpm start` here, then open it in Expo Go or a dev build.
3. Sign in with the server address above and the seed user `jd@codex.local` / `codex-dev-password`. An address without
   a scheme (`codex.example.com`) gets `https://`; `http://` is kept when typed, for dev servers.

Plain `http`: iOS allows it to local and LAN hosts (`NSAllowsLocalNetworking` in `app.json`). Android allows it in Expo
Go and debug builds; a release build would need `usesCleartextTraffic` through `expo-build-properties`, which is not
added, since a real server is https.

Signing in creates a device session (`docs/auth.md`), listed on the web under Account → Signed-in devices.

Device builds go through EAS later. Expo Go runs everything except on-device speech: `react-native-webview`, `expo-audio`
and `expo-file-system` are in Expo SDK 58's bundled native modules (`expo/bundledNativeModules.json`), so the editor and
the recording screen load there. Only `whisper.rn` is not, so transcribing needs a development build (`npx expo prebuild`,
then `npx expo run:ios` or `run:android`). Without one the recording sheet cannot turn speech into text.

Two things the app needs from the server and the device that are easy to miss:

- **Write needs the SPA built into the server's `wwwroot`.** The editor is a page the server serves
  (`/mobile-editor.html`, the second Vite entry of `Codex.Client`) inside a WebView. A server started without the build
  shows the Write tab's error "The editor could not be loaded. (HTTP 404)". Build it once and after editor changes:
  `cd src/Codex.Client && pnpm run build` (it emits into `src/Codex.WebApi/wwwroot`, gitignored). A page that never starts
  shows "The editor did not start" after 10 s, with Retry.
- **Speech models are downloaded on the phone** (Account → Speech models, `/speech-models`): tiny, base and small Whisper
  models from Hugging Face, 75 MB to 470 MB each, one active at a time. Nothing is bundled; recording asks to download one
  first, and transcription runs on the device with no network.
