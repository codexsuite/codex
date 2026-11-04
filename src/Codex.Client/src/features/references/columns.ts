// The table's optional columns. They drop as the screen narrows (design/README.md, References): kind and licence
// first, then quoted words, then uses. Full class names, so Tailwind sees them.
export const allColumns = ['kind', 'usedIn', 'quoted', 'licence'] as const;
export type Column = (typeof allColumns)[number];

export const columnLabels: Record<Column, string> = {
  kind: 'Kind',
  usedIn: 'Used in',
  quoted: 'Quoted',
  licence: 'Licence',
};

/** Width and when the column drops, for its heading and cells alike. */
export const columnClass: Record<Column, string> = {
  kind: 'w-24 max-[1280px]:hidden',
  usedIn: 'w-[118px] max-[860px]:hidden',
  quoted: 'w-[104px] max-[1000px]:hidden',
  licence: 'w-[140px] max-[1280px]:hidden',
};
