export type LabelTone = 'ok' | 'info' | 'warn' | 'danger';

const tones: readonly LabelTone[] = ['ok', 'info', 'warn', 'danger'];

/** The theme colour of a stored label colour, or null when it is not one of the four tones (drawn hollow). */
export function toneOf(color: string | null): LabelTone | null {
  return tones.find((tone) => tone === color) ?? null;
}

/** The colour a label created from the sheet gets (what the desktop gives a new label). */
export const NEW_LABEL_COLOR: LabelTone = 'info';
