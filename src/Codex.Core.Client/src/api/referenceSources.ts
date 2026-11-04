import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.References. status is cleared, needs_check, risk or not_cited; reason is the clearance code
// the UI words. number is null for a source nothing cites.
export interface SourceRow {
  id: string;
  number: number | null;
  title: string;
  authors: string;
  year: number | null;
  publisher: string | null;
  kind: string;
  kindLabel: string;
  licenceLabel: string;
  status: string;
  reason: string;
  uses: number;
  usedIn: string;
  quotedWords: number;
  limit: number;
  cited: boolean;
  /** Who signed the source off and when ("Cleared by JD · 9 Oct"); null when it never was. */
  clearedLabel: string | null;
  /** "Reminder due" / "Reminder due in 14 days" while a sent permission request waits on a reply. */
  reminderLabel: string | null;
  reminderDue: boolean;
}

/** A CSL-JSON name: a person (family + given) or an organisation (literal). */
export interface CslName {
  family?: string;
  given?: string;
  literal?: string;
}

/** The CSL-JSON item citeproc reads (`reference_csl`). Variables beyond these ride along unchanged. */
export interface CslItem {
  id: string;
  type: string;
  title?: string;
  author?: CslName[];
  issued?: { 'date-parts': number[][] };
  [variable: string]: unknown;
}

export interface SourceCsl {
  sourceId: string;
  status: string;
  csl: CslItem;
  creditLine: string | null;
}

export type ExportFormat = 'bibtex' | 'ris' | 'csljson' | 'permissions';

export const sourcesKey = (bookId: string) => ['sources', bookId] as const;

export const sourcesCslKey = (bookId: string) => ['sources-csl', bookId] as const;

export const getSources = (bookId: string, options?: RequestOptions) =>
  api.get<SourceRow[]>(`/api/books/${bookId}/sources`, options);

export const getSourcesCsl = (bookId: string, options?: RequestOptions) =>
  api.get<SourceCsl[]>(`/api/books/${bookId}/sources/csl`, options);

/** The export is a file download: the browser follows this URL, there is no fetch. */
export const sourcesExportUrl = (bookId: string, format: ExportFormat) =>
  `/api/books/${bookId}/sources/export?format=${format}`;

// ---- Source detail panel (C#: Codex.Contracts.References) ----

export type CheckStatus = 'ok' | 'warn' | 'danger';

export type VerdictAction =
  | 'sign_off'
  | 'reopen'
  | 'accept_fair_use'
  | 'set_licence'
  | 'fill_details'
  | 'recheck'
  | 'remove'
  | 'permission'
  | 'none';

export interface SourceVerdict {
  status: string;
  reason: string;
  action: VerdictAction;
  signedOffBy: string | null;
  signedOffLabel: string | null;
}

export interface ClearanceCheck {
  key: string;
  status: CheckStatus;
  title: string;
  detail: string;
}

export type UseKind = 'quote' | 'paraphrase' | 'data' | 'figure' | 'lyric';

export interface SourceUse {
  id: string;
  sceneId: string;
  chapterNumber: number | null;
  chapterTitle: string;
  sceneNumber: number;
  kind: UseKind;
  excerpt: string;
  words: number;
  locator: string | null;
  newSinceSignOff: boolean;
  pending: boolean;
}

export interface SourceEntry {
  id: string;
  name: string;
  categoryId: string;
}

export interface IdentifierCheck {
  field: 'isbn' | 'doi' | 'url';
  status: 'ok' | 'redirect' | 'not_found' | 'error' | 'checking';
  resolvedUrl: string | null;
}

export interface SourceDetails {
  kind: string;
  title: string;
  authorsText: string;
  year: number | null;
  publisher: string | null;
  edition: string | null;
  isbn: string | null;
  doi: string | null;
  url: string | null;
  accessedOn: string | null;
  licence: string;
  rightsHolder: string | null;
  fairUseNote: string | null;
  missing: string[];
  identifiers: IdentifierCheck[];
}

export interface SourceOption {
  code: string;
  label: string;
}

export interface SourceHistoryEntry {
  when: string;
  who: string;
  text: string;
}

export type PermissionStatus = 'draft' | 'sent' | 'granted' | 'refused';

export interface PermissionLetter {
  fileName: string;
  url: string;
}

/** The permission tracker. remindOn is yyyy-MM-dd; the labels are worded by the server. */
export interface Permission {
  id: string;
  status: PermissionStatus;
  to: string;
  letter: string;
  sentLabel: string | null;
  remindOn: string | null;
  remindLabel: string | null;
  remindDue: boolean;
  respondedLabel: string | null;
  creditLine: string | null;
  terms: string | null;
  note: string | null;
  attachment: PermissionLetter | null;
  /** The short chaser for a sent request; null otherwise. */
  followUp: string | null;
  followedUpLabel: string | null;
  feeAmount: number | null;
}

export interface SourceDetail {
  id: string;
  bookId: string;
  number: number | null;
  kind: string;
  kindLabel: string;
  title: string;
  authorsLabel: string;
  year: number | null;
  publisher: string | null;
  verdict: SourceVerdict;
  checks: ClearanceCheck[];
  uses: SourceUse[];
  quotedWords: number;
  limit: number;
  entry: SourceEntry | null;
  details: SourceDetails;
  kinds: SourceOption[];
  licences: SourceOption[];
  history: SourceHistoryEntry[];
  permission: Permission | null;
}

/** The editable fields as sent: `SourceDetails` without the server-worded parts. */
export type SourceDetailsRequest = Omit<SourceDetails, 'fairUseNote' | 'missing' | 'identifiers'>;

/** A null part is left alone; `entry.entryId: null` removes the Codex link. */
export interface PatchSourceRequest {
  details: SourceDetailsRequest | null;
  entry: { entryId: string | null } | null;
}

export interface RecheckField {
  field: string;
  value: string;
  outcome: 'ok' | 'redirect' | 'not_found';
  redirectTo: string | null;
}

export interface RecheckResult {
  fields: RecheckField[];
  source: SourceDetail;
}

export interface ArchivedScene {
  sceneId: string;
  chapterNumber: number | null;
  sceneNumber: number;
  uses: number;
}

export interface ArchiveSourceResult {
  scenes: ArchivedScene[];
}

export const sourceKey = (sourceId: string) => ['source', sourceId] as const;

export const getSource = (sourceId: string, options?: RequestOptions) =>
  api.get<SourceDetail>(`/api/references/${sourceId}`, options);

export const signOffSource = (sourceId: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/sign-off`);

export const reopenSource = (sourceId: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/reopen`);

export const acceptFairUse = (sourceId: string, reason: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/fair-use`, { reason });

export const patchSource = (sourceId: string, request: PatchSourceRequest) =>
  api.patch<SourceDetail>(`/api/references/${sourceId}`, request);

export const setUseKind = (useId: string, kind: UseKind) =>
  api.patch<SourceDetail>(`/api/reference-uses/${useId}`, { kind });

export const recheckSource = (sourceId: string) =>
  api.post<RecheckResult>(`/api/references/${sourceId}/recheck`);

export const acceptRedirect = (sourceId: string, redirectTo: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/recheck/accept`, { redirectTo });

export const archiveSource = (sourceId: string) =>
  api.post<ArchiveSourceResult>(`/api/references/${sourceId}/archive`);

/** Folds a duplicate into `intoId` and answers with the source that stays. */
export const mergeSource = (sourceId: string, intoId: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/merge`, { intoId });

// ---- Permission tracker ----

/** A null part is left alone; an empty `remindOn` removes the reminder. */
export interface PatchPermissionRequest {
  body: string | null;
  to: string | null;
  remindOn: string | null;
  /** Counted from today by the server; wins over `remindOn`. */
  remindInDays?: number;
}

export const draftPermission = (sourceId: string) =>
  api.post<SourceDetail>(`/api/references/${sourceId}/permission`);

export const patchPermission = (requestId: string, request: PatchPermissionRequest) =>
  api.patch<SourceDetail>(`/api/permission-requests/${requestId}`, request);

export const markPermissionSent = (requestId: string) =>
  api.post<SourceDetail>(`/api/permission-requests/${requestId}/sent`);

export interface GrantPermissionRequest {
  letter: File;
  creditLine: string;
  terms: string;
  fee: string;
}

export const grantPermission = (requestId: string, request: GrantPermissionRequest) => {
  const form = new FormData();
  form.append('letter', request.letter);
  form.append('creditLine', request.creditLine);
  form.append('terms', request.terms);
  if (request.fee.trim() !== '') form.append('fee', request.fee.trim());
  return api.upload<SourceDetail>(`/api/permission-requests/${requestId}/granted`, form);
};

export const refusePermission = (requestId: string, note: string) =>
  api.post<SourceDetail>(`/api/permission-requests/${requestId}/refused`, { note });

export const withdrawPermission = (requestId: string) =>
  api.post<SourceDetail>(`/api/permission-requests/${requestId}/withdraw`);

export const markPermissionFollowedUp = (requestId: string) =>
  api.post<SourceDetail>(`/api/permission-requests/${requestId}/followed-up`);

export const attachPermissionLetter = (requestId: string, letter: File) => {
  const form = new FormData();
  form.append('letter', letter);
  return api.upload<SourceDetail>(`/api/permission-requests/${requestId}/letter`, form);
};

export const removePermissionLetter = (requestId: string) =>
  api.del<SourceDetail>(`/api/permission-requests/${requestId}/letter`);
