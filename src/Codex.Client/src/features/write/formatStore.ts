import { useCallback, useState } from 'react';

// The Format popover's settings, remembered per book in this browser (`codex.format.<book>`).

export const widths = ['narrow', 'medium', 'wide'] as const;
export const paragraphStyles = ['spaced', 'indent'] as const;

export interface WriteFormat {
  width: (typeof widths)[number];
  paragraphs: (typeof paragraphStyles)[number];
  hideBeats: boolean;
  hideSummaries: boolean;
  underlineNames: boolean;
}

export const defaultFormat: WriteFormat = {
  width: 'medium',
  paragraphs: 'spaced',
  hideBeats: false,
  hideSummaries: false,
  underlineNames: true,
};

export const formatStorageKey = (bookId: string) => `codex.format.${bookId}`;

function isFormat(value: unknown): value is WriteFormat {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    widths.some((w) => w === v.width) &&
    paragraphStyles.some((p) => p === v.paragraphs) &&
    typeof v.hideBeats === 'boolean' &&
    typeof v.hideSummaries === 'boolean' &&
    typeof v.underlineNames === 'boolean'
  );
}

// Storage can be missing, blocked or hold something else; any of those means the defaults.
export function readFormat(bookId: string): WriteFormat {
  try {
    const raw = window.localStorage.getItem(formatStorageKey(bookId));
    if (raw === null) return defaultFormat;
    const parsed: unknown = JSON.parse(raw);
    return isFormat(parsed) ? parsed : defaultFormat;
  } catch {
    return defaultFormat;
  }
}

function writeFormat(bookId: string, format: WriteFormat) {
  try {
    window.localStorage.setItem(formatStorageKey(bookId), JSON.stringify(format));
  } catch {
    // The setting still applies for this visit; it just will not be remembered.
  }
}

export function useFormat(bookId: string): [WriteFormat, (change: Partial<WriteFormat>) => void] {
  const [state, setState] = useState(() => ({ bookId, format: readFormat(bookId) }));
  // A different book has its own settings: read them when the book changes.
  const current = state.bookId === bookId ? state.format : readFormat(bookId);
  if (state.bookId !== bookId) setState({ bookId, format: current });
  const update = useCallback(
    (change: Partial<WriteFormat>) => {
      const next = { ...current, ...change };
      writeFormat(bookId, next);
      setState({ bookId, format: next });
    },
    [bookId, current],
  );
  return [current, update];
}
