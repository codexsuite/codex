/** `147,964,211` → `148 MB` (decimal megabytes, like the download sizes Hugging Face shows). */
export const formatMegabytes = (bytes: number): string => `${Math.round(bytes / 1_000_000)} MB`;

/** Whole percent of a download, 0 to 100; 0 when the total is not known. */
export function progressPercent(receivedBytes: number, totalBytes: number): number {
  if (totalBytes <= 0) return 0;
  return Math.min(100, Math.max(0, Math.floor((receivedBytes / totalBytes) * 100)));
}
