// The Library toolbar's icons, drawn as in design/screens/library.html. Sized by the control's CSS (13px; 11px in .sel).
const line = { viewBox: '0 0 16 16', fill: 'none', stroke: 'currentColor', 'aria-hidden': true };

export const PlusIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M8 3v10M3 8h10" />
  </svg>
);

export const ImportIcon = () => (
  <svg {...line} strokeWidth="1.5">
    <path d="M8 2v8m0 0-3-3m3 3 3-3M3 11v2h10v-2" />
  </svg>
);

export const SearchIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <circle cx="7" cy="7" r="4.5" />
    <path d="m10.5 10.5 3 3" />
  </svg>
);

export const ChevronDownIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="m4 6 4 4 4-4" />
  </svg>
);

export const GridIcon = () => (
  <svg viewBox="0 0 16 16" fill="currentColor" aria-hidden>
    <rect x="2" y="2" width="5" height="5" rx="1" />
    <rect x="9" y="2" width="5" height="5" rx="1" />
    <rect x="2" y="9" width="5" height="5" rx="1" />
    <rect x="9" y="9" width="5" height="5" rx="1" />
  </svg>
);

export const ListIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M2 4h12M2 8h12M2 12h12" />
  </svg>
);
