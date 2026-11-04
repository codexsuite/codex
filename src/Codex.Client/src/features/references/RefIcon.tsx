// The line icons design/screens/references.html draws inline (`I` and `KIND_I`), on a 16px grid in currentColor.
// Size comes from the className (13px by default, as `.btn svg`).

const paths = {
  plus: <path d="M8 3v10M3 8h10" />,
  chevron: <path d="m4 6 4 4 4-4" />,
  back: <path d="M10 3 5 8l5 5" />,
  close: <path d="m4 4 8 8M12 4l-8 8" />,
  search: (
    <>
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" />
    </>
  ),
  warn: (
    <>
      <path d="M8 2 14.5 13.5h-13z" />
      <path d="M8 6.5v3M8 11.5v.5" />
    </>
  ),
  shield: (
    <>
      <path d="M8 1.5 13.5 3.5v4c0 3.2-2.3 5.8-5.5 7-3.2-1.2-5.5-3.8-5.5-7v-4z" />
      <path d="m5.5 8 1.8 1.8L10.8 6.3" />
    </>
  ),
  flag: <path d="M3.5 14V2.5M3.5 3h8l-1.8 3 1.8 3h-8" />,
  mail: (
    <>
      <rect x="2" y="3.5" width="12" height="9" rx="1.5" />
      <path d="m2.5 4.5 5.5 4 5.5-4" />
    </>
  ),
  ext: (
    <path d="M9 2.5h4.5V7M13.5 2.5 7.5 8.5M12 9.5v3.5a1 1 0 0 1-1 1H3.5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1H7" />
  ),
  spark: <path d="M8 1.5 9.4 6.6 14.5 8 9.4 9.4 8 14.5 6.6 9.4 1.5 8 6.6 6.6z" />,
  book: (
    <>
      <path d="M3 2.5h7.5a2 2 0 0 1 2 2v9H5a2 2 0 0 1-2-2z" />
      <path d="M3 11.5a2 2 0 0 1 2-2h7.5" />
    </>
  ),
  paper: (
    <>
      <path d="M4 1.5h5.5L12.5 4.5v10H4z" />
      <path d="M9.5 1.5v3h3M6 8h4.5M6 10.5h4.5" />
    </>
  ),
  web: (
    <>
      <circle cx="8" cy="8" r="6" />
      <path d="M2 8h12M8 2c1.8 2 1.8 10 0 12M8 2c-1.8 2-1.8 10 0 12" />
    </>
  ),
  report: (
    <>
      <rect x="2.5" y="2" width="11" height="12" rx="1.5" />
      <path d="M5.5 11V8.5M8 11V6M10.5 11V9.5" />
    </>
  ),
  song: (
    <>
      <path d="M6 12V3l7-1.5v9" />
      <circle cx="4.5" cy="12" r="1.8" />
      <circle cx="11.5" cy="10.5" r="1.8" />
    </>
  ),
} as const;

// Heavier strokes: the checklist's marks.
const bold = {
  check: <path d="m3 8.5 3 3 7-7" />,
  cross: <path d="m4 4 8 8M12 4l-8 8" />,
  bang: <path d="M8 3v6M8 12.5v.5" />,
} as const;

const solids = {
  more: (
    <>
      <circle cx="8" cy="3.5" r="1.3" />
      <circle cx="8" cy="8" r="1.3" />
      <circle cx="8" cy="12.5" r="1.3" />
    </>
  ),
} as const;

export type RefIconName = keyof typeof paths | keyof typeof bold | keyof typeof solids;

/** The kind icons, by reference_kind code; an unknown kind has none. */
export const kindIcons: Record<string, RefIconName | undefined> = {
  book: 'book',
  paper: 'paper',
  web: 'web',
  report: 'report',
  song: 'song',
};

export function RefIcon({
  name,
  className = 'size-[13px]',
}: {
  name: RefIconName;
  className?: string;
}) {
  if (name in solids) {
    return (
      <svg
        viewBox="0 0 16 16"
        aria-hidden="true"
        className={`shrink-0 ${className}`}
        fill="currentColor"
      >
        {solids[name as keyof typeof solids]}
      </svg>
    );
  }
  const heavy = name in bold;
  return (
    <svg
      viewBox="0 0 16 16"
      aria-hidden="true"
      className={`shrink-0 ${className}`}
      fill="none"
      stroke="currentColor"
      strokeWidth={heavy ? 2 : 1.5}
    >
      {heavy ? bold[name as keyof typeof bold] : paths[name as keyof typeof paths]}
    </svg>
  );
}
