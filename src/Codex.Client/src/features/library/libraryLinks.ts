import { newBookLink, seriesLink } from '@codex/core/shell/links';

// Library-only URLs built on the shell's builders. The New book dialog is `?book=new` (shell); starting it from a
// Series screen keeps that series in the hash. The Import dialog is `?import=1`.
export const newBookInSeriesLink = (seriesId: string) =>
  `${newBookLink()}${seriesLink(seriesId).slice(1)}`;
export const importLink = () => '/?import=1';
export const newSeriesLink = () => '/?newSeries=1';
