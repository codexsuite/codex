import { api } from './client';

// Sign-in, the signed-in user's account and the admin user management API (#29), personal access tokens for MCP
// (#32), and device sessions: the mobile app's bearer sign-in (#35). C#: Codex.Contracts.Auth.

export interface Me {
  id: string;
  email: string;
  displayName: string;
  /** "JD": first letters of the display name's first and last words. */
  initials: string;
  isAdmin: boolean;
  mustChangePassword: boolean;
}

export type AdminUserStatus = 'active' | 'disabled' | 'locked';

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  status: AdminUserStatus;
  mustChangePassword: boolean;
  lastLoginAt: string | null;
  lastLoginLabel: string | null;
  createdAt: string;
  createdLabel: string;
  /** Personal access tokens that still work (not revoked, not expired). */
  tokenCount: number;
}

export interface OneTimePassword {
  user: AdminUser;
  oneTimePassword: string;
}

export interface UpdateUserRequest {
  isAdmin?: boolean;
  disabled?: boolean;
  unlock?: boolean;
}

/** What a personal access token may do over MCP. Every token holds codex:read. */
export type ApiTokenScope = 'codex:read' | 'codex:write' | 'codex:references' | 'codex:generate';

export interface ApiToken {
  id: string;
  name: string;
  /** "cdx_pat_" and the first 8 characters after it. */
  prefix: string;
  scopes: ApiTokenScope[];
  expiresAt: string | null;
  /** null: never expires. */
  expiresLabel: string | null;
  expired: boolean;
  lastUsedAt: string | null;
  /** null: never used. */
  lastUsedLabel: string | null;
  createdAt: string;
  createdLabel: string;
}

export interface CreatedApiToken {
  token: ApiToken;
  /** The whole token, shown once: the server keeps only its hash. */
  secret: string;
}

/** A signed-in device (the mobile app): a bearer session listed under Account → Signed-in devices. */
export interface DeviceSession {
  id: string;
  /** What the device called itself at sign-in ("JD's iPhone"). */
  name: string;
  /** "cdx_dev_" and the first 8 characters after it. */
  prefix: string;
  /** True for the session this request was made with. */
  current: boolean;
  /** Sliding: each use moves it 90 days on. */
  expiresAt: string;
  expiresLabel: string;
  lastUsedAt: string | null;
  /** null: never used. */
  lastUsedLabel: string | null;
  createdAt: string;
  createdLabel: string;
}

export interface CreatedDeviceSession {
  session: DeviceSession;
  /** The bearer secret (`cdx_dev_…`), shown once: the server keeps only its hash. */
  secret: string;
  me: Me;
}

export const meKey = ['auth', 'me'] as const;
export const usersKey = ['admin', 'users'] as const;
export const tokensKey = ['auth', 'tokens'] as const;
export const deviceSessionsKey = ['auth', 'device-sessions'] as const;

export const login = (email: string, password: string) =>
  api.post<Me>('/api/auth/login', { email, password });
export const logout = () => api.post<void>('/api/auth/logout');
export const getMe = (signal?: AbortSignal) => api.get<Me>('/api/auth/me', { signal });
export const updateMe = (displayName: string) => api.patch<Me>('/api/auth/me', { displayName });
export const changePassword = (currentPassword: string, newPassword: string) =>
  api.post<Me>('/api/auth/password', { currentPassword, newPassword });

export const listUsers = (signal?: AbortSignal) =>
  api.get<AdminUser[]>('/api/admin/users', { signal });
export const createUser = (email: string, displayName: string) =>
  api.post<OneTimePassword>('/api/admin/users', { email, displayName });
export const updateUser = (id: string, request: UpdateUserRequest) =>
  api.patch<AdminUser>(`/api/admin/users/${id}`, request);
export const resetPassword = (id: string) =>
  api.post<OneTimePassword>(`/api/admin/users/${id}/reset-password`);
export const revokeUserTokens = (id: string) =>
  api.post<void>(`/api/admin/users/${id}/revoke-tokens`);

export const listTokens = (signal?: AbortSignal) =>
  api.get<ApiToken[]>('/api/auth/tokens', { signal });
/** expiresInDays null: the token never expires. */
export const createToken = (name: string, scopes: ApiTokenScope[], expiresInDays: number | null) =>
  api.post<CreatedApiToken>('/api/auth/tokens', { name, scopes, expiresInDays });
export const revokeToken = (id: string) => api.del<void>(`/api/auth/tokens/${id}`);

/** The mobile app's sign-in: same checks, lockout and rate limits as `login`, but answers with a bearer secret. */
export const createDeviceSession = (email: string, password: string, deviceName: string) =>
  api.post<CreatedDeviceSession>('/api/auth/device-sessions', { email, password, deviceName });
export const listDeviceSessions = (signal?: AbortSignal) =>
  api.get<DeviceSession[]>('/api/auth/device-sessions', { signal });
export const revokeDeviceSession = (id: string) => api.del<void>(`/api/auth/device-sessions/${id}`);
/** Ends the session this request is signed in with (the mobile app's Sign out). */
export const endCurrentDeviceSession = () => api.del<void>('/api/auth/device-sessions/current');

/** A `returnUrl` is followed only when it is a path on this site (`/…`, never `//host` or `/\host`). */
export function safeReturnUrl(value: string | null): string {
  if (
    value === null ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.startsWith('/\\')
  ) {
    return '/';
  }
  return value;
}

export function loginLink(returnUrl: string): string {
  return returnUrl === '/' ? '/login' : `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}
