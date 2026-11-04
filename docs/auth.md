# Sign-in, access and users (#29, #80)

Codex has email-and-password sign-in and a user management page for admins. There is **no sign-up**: the first admin
exists up front, and an admin adds everyone else from `/settings/users`. Google sign-in comes in a later issue. Remote
MCP auth is #32: personal access tokens are below (*MCP*); OAuth is its phase 2.

## Dev and test credentials

The seed (`db/seed/08_auth.sql`) gives JD a password and makes JD an admin:

| Email | Password |
|---|---|
| `jd@codex.local` | `codex-dev-password` |

Dev and test only: the app never applies the seed, and no password hash for production is committed.

## The first admin in production

Set both variables for the first boot:

| Variable | What it does |
|---|---|
| `CODEX_ADMIN_EMAIL` | The admin's email. The user is created if no user has it. |
| `CODEX_ADMIN_PASSWORD` | Their password (at least 10 characters). |

At startup (`AdminBootstrap`), if that user has no password yet, the app sets it and makes the user an admin. A user who
already has a password is left alone, so leaving the variables set does nothing. Remove them after the first boot
anyway.

## How a request is signed in

- `POST /api/auth/login` with `{ email, password }` sets the `codex.auth` cookie: HttpOnly, `SameSite=Lax`, a 14-day
  sliding expiry, and Secure whenever the request is https. Behind Cloudflare the app sees http from the tunnel, so
  `UseForwardedHeaders` applies `X-Forwarded-Proto` and `X-Forwarded-Host`. The browser never holds a token.
- The mobile app signs in with a **device session** instead (`POST /api/auth/device-sessions`, *Device sessions*): it
  sends `Authorization: Bearer cdx_dev_…` on `/api` and `/hubs` and holds no cookie. A request with that bearer is
  authenticated by the `DeviceBearer` scheme, any other by the cookie (`DeviceOrCookie` is the default scheme).
- Every endpoint needs a signed-in user (the fallback policy) unless it is marked `AllowAnonymous`: `/login`,
  `POST /api/auth/login`, `POST /api/auth/device-sessions`, `POST /api/auth/logout`, `/health`, `/api/health`, `/mcp`, which checks its own bearer
  tokens (*MCP*), and `PUT /api/asset-uploads/{token}`, whose single-use link is its credential (*MCP*). Static assets are served before authorization runs.
- With no session, `/api/*`, `/hubs/*` and `/mcp` answer **401** `unauthorized` (so does a device secret that is no
  longer good). A page load is redirected to
  `/login?returnUrl=<path>` instead. A signed-in user without the right gets **403** `forbidden`.
- Each request's cookie is checked against `app_user`, cached for up to 30 seconds per process
  (`UserSessions`, which forgets a user as soon as this process changes them). A disabled user, or a cookie issued
  before the user's `security_stamp` changed, is signed out. A password change or an admin reset changes the stamp.
- `ICurrentUser.Id` reads the cookie's (or device session's) NameIdentifier claim. Nothing takes a user id as a parameter.
- The cookie's Data Protection keys are stored in the `data_protection_key` table, so a redeploy keeps everyone signed
  in.

### Refusals and lockout

A failed sign-in always gets the same 401 and the same sentence, whatever the reason: unknown email, wrong password,
no password, disabled, locked or rate limited. Ten wrong passwords in a row lock the account for 15 minutes, and an
admin can unlock it sooner. Sign-in is rate limited to 30 attempts a minute per IP (`CF-Connecting-IP` behind
Cloudflare) and 20 attempts per 15 minutes per email.

### CSRF

The cookie is `SameSite=Lax`. A write to `/api/*` that carries the cookie must also send either a JSON body or the
`X-Codex-Client` header (`CrossSiteWriteGuard`). The SPA's fetch wrapper sends that header on every request. A
cross-site HTML form can send neither, so it gets 403. Uploads and bodiless POSTs rely on the header.

## The access rule

A book or series, and everything inside it, belongs to `book.owner_id`, and its owner may share it with other users
(#80): a **grant** gives one user the role **view**, **comment** or **write** on one book or on a whole series. The rule
lives in the application layer, not on the HTTP routes, because the MCP tools call the same commands and queries as the
API.

- **One definition.** The `book_access (book_id, user_id, role, role_rank)` view combines the owner (`owner`), direct
  grants, a series' grants (inherited by each of its books, including books added later) and `view` on a series row for
  anyone with a grant on one of its books, so the book can read the series' shared Knowledge base, labels and style
  guide. The highest role wins (`role_rank`: view 1, comment 2, write 3, owner 4). The owner has no grant row
  (`book_grant` has a trigger for that). Every check and every "what can this user see" list reads this view.
- **Every command and query states its minimum role.** Anything that takes a book id, or the id of anything inside a
  book, calls `IBookAccess.RequireAsync(bookId, BookRole.<minimum>)` or `RequireAsync(BookItem.<Kind>, id, minimum)`
  first, before validating the body. Reads need **view**; content and book-settings commands, image uploads, drafts,
  export and proof need **write**; comments (#26) will need **comment**; archive, delete, duplicate, publish and new
  edition, moving a book between series, renaming, reordering or deleting a series, and managing sharing need
  **owner**. Series-scoped items (KB categories and entries, labels, the style guide on the series row) resolve to the
  series row, so editing them needs write on the series; a grant on one book gives view there.
- **No access at all is `not_found`** (404, MCP `not_found`), the same as an id that does not exist, so a stranger
  cannot tell a book exists. **A role below the minimum is `forbidden`** (403, MCP `forbidden`) with a fixed sentence.
- **Lists and searches** read `book_access`: the Library lists the books you own and the books shared with you, each
  with your `role` and its `owner`. A series shared with you shows all its books; a book shared alone shows under its
  series' heading without the others, and the series' counts cover only the books you can open. The archived shelf
  lists only your own books.
- Some checks stay on `owner_id` on purpose: things that belong to a user rather than a book (assets with no book,
  imports, layout templates, AI integrations, tokens), and owner-only actions (a new book or series joins or is made
  from the caller's own series and books only).
- **Sharing** (`Book settings → Sharing`):

| Endpoint | Who | What |
|---|---|---|
| `GET /api/books/{id}/sharing` | view+ | `{ owner, role, book: [{ user, role }], series: { id, title, grants } \| null }`; emails only for the owner |
| `POST /api/books/{id}/sharing/lookup` | owner | `{ email }` → `{ id, displayName, email }` or 404: exact, case-insensitive, trimmed; a disabled user is 404. 20 a minute and 200 a day per user, then 429 `rate_limited` |
| `PUT /api/books/{id}/sharing/{userId}` | owner | `{ role }`: adds or changes a grant; `id` may be the series id. The owner or an archived book 409, an unknown or disabled user 404 |
| `DELETE /api/books/{id}/sharing/{userId}` | owner | archives the grant on `id` (404 if none); sharing again revives it |

- **Archiving a book archives its grants** (`book_grant.archived_at`); restoring brings none back. Grants on its series stay, so series
  collaborators keep the archived book. Deleting a book or series archives its grants with it.
- **Moving a book into or out of a series** changes who inherits access. The move's preview counts the people the target
  series is shared with (`targetSeriesSharedWith`) and those who see the book only through its current series
  (`losingAccess`).
- **Changes reach open tabs.** Each grant change, archive, series move or series delete publishes `AccessChanged
  { bookId, userId, role | null }` for every book whose effective role changed for a user. The hub sends it to that
  user's connections only (never to the book's group), and when `role` is null it first removes those connections from
  the book's group.
- `ProseStore` checks access itself when it reads or locks a scene, so every feature that writes prose is covered.
- Joining a book's SignalR group (`/hubs/book`, which requires sign-in) needs view.
- Admins get no access to other people's books.
- Work that outlives its request, such as an MCP background draft, carries its user with `CurrentUser.ActAs(userId)`
  and checks the role again when it runs.
- In code and in the UI this is *sharing*, *access* or *grant*, never *permission*, which means rights clearance in
  References.

## MCP

`/mcp` takes a **personal access token** (#32): `Authorization: Bearer cdx_pat_…`. The token's user goes on the
request as its principal, so `CurrentUser`, and with it the access rule, apply to MCP too, and the token's scopes
limit which tools it may call (`docs/mcp.md` → *Scopes*). A missing, unknown, revoked or expired token, or a disabled
user's, gets 401. The cookie routes (`/api/*`, `/hubs/*`) never accept a token.

- A token is `cdx_pat_` and 43 base62 characters. Only its SHA-256 is stored (`api_token.token_hash`), so it is shown
  once. `token_prefix` (`cdx_pat_` and the next 8 characters) is what the Account page shows.
- `codex:read` is always granted; `codex:write`, `codex:references` and `codex:generate` are chosen. Expiry is 30
  days, 90 days (the default), a year, or never.
- Revoking sets `revoked_at`; nothing is deleted, so History keeps the token's name.
- `create_image_upload` (#65) mints a single-use link to `PUT /api/asset-uploads/{token}` for one book, as the calling
  token's user. The upload runs as that user (`CurrentUser.ActAs`), and the link stops working when the token is revoked
  or expires or the user is disabled, as well as after 10 minutes or one upload (`docs/mcp.md` → *Upload links*).

A device secret (`cdx_dev_…`) is not accepted on `/mcp`, and a personal access token is not accepted on `/api` or `/hubs`.

The stdio process and, in Development only, a tokenless `/mcp` request from this machine act as one configured user:
`CODEX_MCP_TOKEN`'s user with its scopes when that is set, else `CODEX_MCP_USER_EMAIL` (default `jd@codex.local`, which
must be an active user) with every scope.

## Device sessions (mobile app, #35)

The Expo app cannot rely on a cookie, so it signs in once for a **device session**: a bearer secret tied to one user and
one device name, listed on the Account page under Signed-in devices.

- `POST /api/auth/device-sessions` with `{ email, password, deviceName }` runs the same checks as `/api/auth/login`
  (`SignInService`: the per-email limit, the lockout, the one refusal sentence, 30 sign-ins a minute per IP), so a refusal
  is the same 401, and a wrong password counts toward the same lockout. `deviceName` is trimmed and must be 1 to 100
  characters, else 400 `validation` (checked before the password, so a bad name costs no attempt). It answers **201**
  with `{ session, secret, me }`, sets **no cookie**, and the secret is shown once.
- The secret is `cdx_dev_` and 43 base62 characters. Only its SHA-256 is stored (`device_session.token_hash`).
  `token_prefix` (`cdx_dev_` and the next 8 characters) is what the list shows.
- **Sliding 90 days.** `expires_at` starts 90 days out and each use moves it to 90 days from then, written at most once a
  minute per session (as is `last_used_at`). An unused session expires.
- **What ends a session** (the next request is 401 `unauthorized`): it is revoked (`revoked_at`; nothing is deleted),
  it expires, its user is disabled, or the user's `security_stamp` differs from the one recorded at sign-in (a password
  change or an admin reset ends device sessions as it ends cookies; unlike the cookie, a password change ends the
  changing device's session too, because it has no cookie to re-issue). The check reads `app_user` on each request.
- **Where it works.** `Authorization: Bearer cdx_dev_…` on `/api/*` and `/hubs/*`; on `/hubs/*` only, SignalR's
  `access_token` query value too (WebSockets cannot send headers). Never on `/mcp`, and a `cdx_pat_` token is still 401
  on `/api`. The principal carries the user's NameIdentifier (so `ICurrentUser` is unchanged) and the session id in the
  `codex:device-session` claim. `CrossSiteWriteGuard` does not apply: a bearer request carries no cookie.
- A device can list and revoke the user's sessions, and `DELETE /api/auth/device-sessions/current` ends the session the
  request was made with (404 `not_found` when the request is not signed in with one, e.g. a browser).

## Users and accounts

| Endpoint | Who | What |
|---|---|---|
| `GET /api/auth/me` | signed in | `{ id, email, displayName, isAdmin, mustChangePassword }` |
| `PATCH /api/auth/me` | signed in | `{ displayName }` |
| `POST /api/auth/password` | signed in | `{ currentPassword, newPassword }`: signs every other session out and re-issues this one |
| `POST /api/auth/logout` | anyone | clears the cookie |
| `GET /api/admin/users` | admin | the user table |
| `POST /api/admin/users` | admin | `{ email, displayName }`: returns the user and a one-time password, shown once |
| `PATCH /api/admin/users/{id}` | admin | `{ isAdmin?, disabled?, unlock? }` |
| `POST /api/admin/users/{id}/reset-password` | admin | a new one-time password; signs that user out everywhere |
| `POST /api/admin/users/{id}/revoke-tokens` | admin | revokes every personal access token the user has |
| `GET /api/auth/tokens` | signed in | your tokens that are not revoked, newest first (expired ones too, marked `expired`) |
| `POST /api/auth/tokens` | signed in | `{ name, scopes, expiresInDays }` (null: never): the token and its secret, shown once |
| `DELETE /api/auth/tokens/{id}` | signed in | revokes one of your tokens; another user's is 404 |
| `POST /api/auth/device-sessions` | anyone | `{ email, password, deviceName }`: the mobile sign-in; `{ session, secret, me }`, secret shown once, no cookie |
| `GET /api/auth/device-sessions` | signed in | your sessions that are not revoked or expired, newest first, `current` marking the one the request came with |
| `DELETE /api/auth/device-sessions/{id}` | signed in | revokes one of your sessions; another user's is 404 |
| `DELETE /api/auth/device-sessions/current` | device session | ends the session the request was made with; any other sign-in is 404 |

The `Admin` policy reads `is_admin` from `app_user` rather than from a claim, so a change takes effect on the next
request. Non-admins get 403, and the Users item is hidden from their menu. The API enforces the guards, not just the
UI: you cannot disable yourself or remove your own admin rights, and at least one active admin always remains (409
`conflict`). There is no hard delete, because a user owns books; disable the user instead.

A user created or reset by an admin has `must_change_password` set. The client then keeps every route on
`/account/password` until the user changes it (`AuthGate`).

## Client

- `/login` (`features/auth/LoginPage.tsx`) goes to `returnUrl` afterwards, but only for a path on this site
  (`safeReturnUrl`). Anything else opens the Library.
- `@codex/core/api/client` reports any 401 to the platform, except for the sign-in call itself; the web platform
  (`src/webPlatform.ts`) sends the page to `/login?returnUrl=…` unless it is already on `/login`.
- `AuthGate` waits for `/api/auth/me` before rendering any other page.
- `UserMenu` is the avatar at the right of the Library and book bars. It holds Account, Users (admins only) and Sign
  out, and renders nothing until `/api/auth/me` has answered.
- `/account` holds the display name, the password and **Agent access** (personal access tokens: create one with a
  name, scopes and expiry, copy it once, revoke any). `/settings/users` is the user management page; it shows each
  user's working token count and a Revoke tokens button.

## Tests

- Every test host gets `TestAuthentication` through `ConfigureTestServices`. Production has no test switch.
  - A request without the `codex.auth` cookie is signed in as the fixture's `SignedInAs`, which is `TestUser.Jd` by
    default, so existing tests run unchanged.
  - Override `SignedInAs` with `TestUser.Of(id)` or `TestUser.Anonymous` to change that for a class.
  - Send the `X-Test-User` header (`client.ActingAs(id)`) to change it for one client.
  - A request that carries the cookie is authenticated by the cookie, as in production.
- An API test that calls a service straight from `Host.Services` acts as `SignedInAs` (`ApiFixture` wraps each test in
  `CurrentUser.ActAs`).
- `TestUsers.CreateAsync` adds another user, and `TestUsers.DevPassword` is JD's seeded password. The seed's second user,
  Ana (`SeedIds.Users.Ana`, `ana@codex.local`, the same dev password), views Product Engineering and edits the Forsaken
  Gods series; `TestRows.GrantAsync` adds a grant.
- **The access sweep.** `AccessSweep` lists every id-bearing `/api` route with the least role it needs (or `Own` for a
  user's own things) and a URL naming JD's item. `AccessSweepTests` calls each as a stranger and expects 404;
  `AccessSweepRoleTests` calls each as a viewer, a commenter and an editor and expects 403 where the role is too low and
  neither 403 nor a 404 that JD would not get otherwise. A new `/api` route without a row (or a place in `NoBookIds`)
  fails `Endpoints_ShouldAllBeSwept`.
