// The Plan screen's icons, drawn as in design/screens/plan.html (`I` there). Sized by the control's CSS.
const line = { viewBox: '0 0 16 16', fill: 'none', stroke: 'currentColor', 'aria-hidden': true };

export const PlusIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M8 3v10M3 8h10" />
  </svg>
);

export const ChevronDownIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="m4 6 4 4 4-4" />
  </svg>
);

export const SearchIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <circle cx="7" cy="7" r="4.5" />
    <path d="m10.5 10.5 3 3" />
  </svg>
);

export const BoardIcon = () => (
  <svg viewBox="0 0 16 16" fill="currentColor" aria-hidden>
    <rect x="1.5" y="2" width="4" height="12" rx="1" />
    <rect x="6.5" y="2" width="4" height="8" rx="1" />
    <rect x="11.5" y="2" width="3" height="10" rx="1" />
  </svg>
);

export const OutlineIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M2 4h12M5 8h9M5 12h9" />
  </svg>
);

export const ArchiveIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <rect x="2" y="3" width="12" height="3" rx="1" />
    <path d="M3 6v6.5a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1V6M6.5 9h3" />
  </svg>
);

export const TagIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <path d="M2.5 2.5h5l6 6-5 5-6-6z" />
    <circle cx="5.5" cy="5.5" r="1" />
  </svg>
);

export const PenIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <path d="M10.5 2.5 13.5 5.5 6 13H3v-3z" />
  </svg>
);

export const MoreIcon = () => (
  <svg viewBox="0 0 16 16" fill="currentColor" aria-hidden>
    <circle cx="8" cy="3.5" r="1.3" />
    <circle cx="8" cy="8" r="1.3" />
    <circle cx="8" cy="12.5" r="1.3" />
  </svg>
);

export const ImageIcon = () => (
  <svg {...line} strokeWidth="1.3">
    <rect x="2.5" y="3" width="11" height="10" rx="1" />
    <circle cx="6" cy="6.5" r="1.2" />
    <path d="m3 12 3.5-3.5 2.5 2.5 1.5-1.5L13 12" />
  </svg>
);
