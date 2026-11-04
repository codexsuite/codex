// Shared DTOs (C#: Codex.Contracts.Books, Codex.Contracts.Sharing). Timestamps are ISO strings; labels are
// server-formatted.

/** Who owns a book: the Library's "Shared by …" line. */
export interface BookOwner {
  id: string;
  displayName: string;
}

export interface LibraryBook {
  id: string;
  title: string;
  subtitle: string | null;
  authorName: string | null;
  seriesId: string | null;
  seriesName: string | null;
  seriesNumber: number | null;
  status: string;
  coverPalette: string;
  coverInitials: string;
  wordCount: number;
  targetWords: number | null;
  progressPct: number | null;
  lastEditedAt: string;
  lastEditedLabel: string;
  lastScreen: string | null;
  archived: boolean;
  /** The uploaded cover image; null means the generated cover in `coverPalette`. */
  coverUrl: string | null;
  /** Published, and the manuscript changed after its newest snapshot. */
  editedSincePublication: boolean;
  /** The caller's role on the book (`parseBookRole`); anything but `owner` means it is shared with them. */
  role: string;
  owner: BookOwner;
}

export interface LibrarySeries {
  id: string;
  name: string;
  description: string | null;
  bookCount: number;
  publishedCount: number;
  wordCount: number;
  lastEditedAt: string;
  lastEditedLabel: string;
  books: LibraryBook[];
  /** #80: reached only through grants on some of its books, so the shelf shows just those. */
  partlyShared: boolean;
  /** True only in the Archived view, for a series that is itself archived (its books read as archived with it). */
  archived: boolean;
}

export interface Library {
  series: LibrarySeries[];
  standalone: LibraryBook[];
}

export interface BookScopeEntry {
  id: string;
  kind: string;
  title: string;
}

export interface BookHeader {
  id: string;
  title: string;
  subtitle: string | null;
  authorName: string | null;
  seriesId: string | null;
  seriesName: string | null;
  seriesNumber: number | null;
  status: string;
  coverPalette: string;
  coverInitials: string;
  wordCount: number;
  targetWords: number | null;
  scope: BookScopeEntry[];
  referenceBadgeCount: number | null;
  /** `danger` when the count is copyright risks, `warn` when it is sources to check; null when the count is 0 or null. */
  referenceBadgeTone: string | null;
  lastEditedAt: string;
  lastEditedLabel: string;
  /** The uploaded cover image (web rendition once made, else the original); null means the generated cover. */
  coverUrl: string | null;
  /** The caller's role on the book (`parseBookRole`): below `write` every screen is read-only. */
  role: string;
  owner: BookOwner;
}

/** A person on the Sharing card. `email` is null unless the caller owns the book. */
export interface SharingUser {
  id: string;
  displayName: string;
  email: string | null;
}

/** One grant: `view`, `comment` or `write`, never `owner` (the owner has no grant row). */
export interface SharingGrant {
  user: SharingUser;
  role: string;
}

/** The grants on the book's series, which reach every book in it (including books added later). */
export interface SharingSeries {
  id: string;
  title: string;
  grants: SharingGrant[];
}

/** `GET /api/books/{id}/sharing`. `role` is the caller's own; only `owner` may change anything. */
export interface Sharing {
  owner: SharingUser;
  role: string;
  book: SharingGrant[];
  series: SharingSeries | null;
}

/** `POST /api/books/{id}/sharing/lookup`: one exact address (case and surrounding spaces ignored). */
export interface SharingLookupRequest {
  email: string;
}

/** `PUT /api/books/{id}/sharing/{userId}`; `id` may be the series id for the "Whole series" list. */
export interface SetGrantRequest {
  role: string;
}
