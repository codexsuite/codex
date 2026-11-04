// The error body every endpoint returns (C#: Codex.Contracts.Errors.ApiError).
export const apiErrorCodes = [
  'not_found',
  'ambiguous',
  'conflict',
  'out_of_scope',
  'blocked',
  'needs_confirmation',
  'validation',
  'not_configured',
  'unauthorized',
  'forbidden',
  'rate_limited',
  'internal_error',
] as const;

export type ApiErrorCode = (typeof apiErrorCodes)[number];

export interface ApiError {
  error: string;
  code: ApiErrorCode;
}

export function isApiError(value: unknown): value is ApiError {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return (
    typeof candidate.error === 'string' &&
    typeof candidate.code === 'string' &&
    (apiErrorCodes as readonly string[]).includes(candidate.code)
  );
}
