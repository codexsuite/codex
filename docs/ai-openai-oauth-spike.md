# Spike: OpenAI "Sign in with ChatGPT" for Codex (issue #36, option 3)

Date: 2026-10-22. Research only, no code. Claims are dated; items I could not verify are marked **unverified**.

## Answer in one line

A sanctioned third-party flow now exists, but only for open-source apps and personal projects that run locally. Paid or remotely hosted apps must join an OpenAI waitlist. Build option 3 later, behind options 1 and 2, and only if the conditions below are met. Never reuse the Codex CLI client id.

## 1. Is there a third-party flow?

Yes, since DevDay on 2026-09-29 (announced; WorkOS write-up dated 2026-10-01). OpenAI calls it "Sign in with ChatGPT" (SIWC) and documents it at developers.openai.com/siwc. Three routes:
- Identity sign-in on a website. "Currently offered to a select group of commercial partners."
- Sign-in inside a ChatGPT plugin.
- **Plan usage in an open-source app.** "At launch" available to "open-source projects, personal projects that run locally, and selected private apps." For "paid or remotely hosted apps", the docs say to join the waitlist (cookbook, read 2026-10-22).

How it works for third parties:
- OAuth 2.0 with PKCE against `https://auth.openai.com`, loopback callback listener on `127.0.0.1`.
- Client ids are issued per user and workspace during sign-in, not hand-registered by the developer (new profiles send `client_id=dynamic_agent_client`; `ext_agent_host_id` identifies the host). No client secret or API key. The SIWC landing page also says "Request a client ID" with application details and callback URLs, so the exact process is **unverified**.
- Scopes: `openid profile email offline_access`, plus `resource.invoke` with `chatgpt.tokens.use.direct`, resource `https://api.openai.com/v1`. A sign-in alone does not grant plan usage; the granted scope must include `chatgpt.tokens.use.direct`.
- Only ChatGPT Plus and Pro users are eligible.
- OpenAI's DevKit (`openai/sign-in-with-chatgpt-devkit`, `@siwc/local`) is for "open-source apps that run on a user's own machine" and is under a **Noncommercial License v1.0**.

**Codex CLI itself:** it uses `auth.openai.com` PKCE, a localhost callback (port 1455, fallback 1457, `/auth/callback`), scopes `openid profile email offline_access api.connectors.read api.connectors.invoke`, and a first-party client id registered against a "Codex CLI Hydra redirect URI allow-list" (openai/codex `codex-rs/login/src/server.rs`, read 2026-10-22). I did not retrieve the client id value. Its docs say Codex access tokens are for trusted automation and that general API calls should use Platform API keys. Reusing that id in Codex is not sanctioned anywhere I found. The only OpenAI staff comment (openai/codex discussion #8338, 2026-02-09, an engineer, "not a lawyer") was informal and said to get legal advice for commercial use. We will not do it, and now do not need to.

## 2. What the token can do

- API: the public **Responses API** with the OAuth access token. Chat Completions: **unverified** (docs only mention Responses). Models: whatever `listModels()` returns for the connection; no list published in the pages I read.
- Constraints on plan-funded requests (WorkOS, 2026-10-01; matches the preview): `store: false` and `stream: true` required; no explicit system-role items; `temperature` and `max_output_tokens` must be omitted; no image generation, file search, Code Interpreter, hosted MCP connectors. Prose streaming fits, but the system prompt must travel another way (for example as a user or developer item; **unverified**).
- Billing: counts against the user's ChatGPT plan usage (Codex and ChatGPT Work usage) or available credits. Not API credits. No cost to the app owner.
- Limits: Plus has a 5-hour window shared across all connected apps; Pro has weekly limits only. Users set a per-app weekly cap in Settings > Usage. Errors: 429 `subscription_sharing_usage_limit_exceeded` (cause not distinguished), 403 `subscription_sharing_user_not_eligible`. Exact message counts are **unverified**.
- Lifetimes: access token 1 hour; refresh token 30 days, rotated on every refresh (new 30-day life). Refresh at `https://auth.openai.com/api/accounts/oauth/token` with `resource=https://api.openai.com/v1`; serialize refreshes per session.
- Revocation: app can call the `revocation_endpoint` from the OpenID config. Users can disconnect in ChatGPT settings, and **OpenAI does not notify the app**; we find out on a failed request or refresh (`invalid_grant`, `refresh_token_expired`, `refresh_token_reused`).
- Storage rule from the docs: tokens in "protected local or self-hosted runtime storage", never browser storage, logs or URLs. That matches our encrypted `credential` column.

## 3. Alternatives

- **Apps SDK / Apps in ChatGPT OAuth**: runs the other way. ChatGPT is the OAuth client to *our* MCP server (CIMD or DCR). It authorizes users to use Codex tools from ChatGPT; it gives Codex no model access. Relevant to the MCP work, not to option 3.
- **Platform API keys**: still the sanctioned path for general API calls. I found no org-admin OAuth flow that mints API keys for third parties (**unverified**: absence of evidence only).
- **OpenRouter** (option 2) offers its own OAuth-style key flow; out of scope here.

## 4. Recommendation

**Build option 3 later, not now. Do not drop it.**
- Ship options 1 (OpenAI-compatible) and 2 (OpenRouter) first; they cover everyone.
- Option 3 is viable only if Codex is distributed as an open-source app that users run themselves (self-hosted, tokens stored encrypted on that user's own server). If Codex becomes a hosted service for other people, it needs OpenAI's waitlist approval first; join it if that is the plan.
- Triggers to revisit: (a) we confirm the eligibility reading in writing with OpenAI (the "selected private apps" wording is ambiguous for a self-hosted multi-user server), (b) the preview constraints (`store:false`, no system role) are workable for `IProseGenerator`, (c) the noncommercial DevKit licence does not matter because we implement the OAuth flow ourselves (**unverified**: check the licence and terms before copying any DevKit code).
- Build nothing for it now: `ai_integration.kind` does not list `openai_oauth` (an additive migration adds it when built), and update issue #36 to say "later, gated on the above" rather than "investigate first".
- Design notes if built: store refresh token encrypted, rotate on each refresh, per-integration refresh lock, treat disconnect as discovered-on-failure, surface 429 as retryable with the app-cap hint.

## Sources (all read 2026-10-22)

- Sign in with ChatGPT overview: https://developers.openai.com/siwc
- Cookbook, integrating SIWC in an open-source app: https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt
- Token reference: https://developers.openai.com/siwc/token-sharing-open-source/token-reference
- Profiles and sessions: https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions
- Errors and recovery: https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery
- DevKit repo: https://github.com/openai/sign-in-with-chatgpt-devkit
- Codex auth docs (redirects from developers.openai.com/codex/auth): https://learn.chatgpt.com/docs/auth
- Codex CLI login code: https://github.com/openai/codex/blob/main/codex-rs/login/src/server.rs
- Staff comment on reuse: https://github.com/openai/codex/discussions/8338
- Apps SDK auth: https://developers.openai.com/apps-sdk/build/auth
- Secondary (not OpenAI, used for dates and preview constraints): https://workos.com/blog/sign-in-with-chatgpt-plan-usage-scope ; https://thenewstack.io/sign-in-with-chatgpt/ (body not retrievable)
- Not retrieved: OpenAI help center and terms pages (**unverified** against them).
