import type { LabelTone } from './LabelPicker';

const tones: readonly LabelTone[] = ['ok', 'info', 'warn', 'danger'];

/** The semantic tone of a stored label colour, or null when the colour is not one of the four (drawn hollow). */
export function toneOf(color: string | null): LabelTone | null {
  return tones.find((t) => t === color) ?? null;
}
