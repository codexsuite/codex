// Status tones for source markers, shared with References: cleared is ok, needs check is warn, risk is danger.
export const markerTone = (status: string): 'ok' | 'warn' | 'danger' =>
  status === 'cleared' ? 'ok' : status === 'risk' ? 'danger' : 'warn';

export const statusLabel = (status: string) =>
  status === 'cleared' ? 'Cleared' : status === 'risk' ? 'Copyright risk' : 'Needs check';

// `quote_over_limit` -> "Quote over limit".
export const reasonLabel = (reason: string) => {
  const words = reason.replaceAll('_', ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
};
