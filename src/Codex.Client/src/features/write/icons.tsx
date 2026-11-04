// The Write screen's icons, drawn as in design/screens/write.html (`I` there). Sized by the control's CSS.
const line = { viewBox: '0 0 16 16', fill: 'none', stroke: 'currentColor', 'aria-hidden': true };

export const ChevronLeftIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M10 3 5 8l5 5" />
  </svg>
);

export const ChevronRightIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="m6 3 5 5-5 5" />
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

export const MoreIcon = () => (
  <svg viewBox="0 0 16 16" fill="currentColor" aria-hidden>
    <circle cx="8" cy="3.5" r="1.3" />
    <circle cx="8" cy="8" r="1.3" />
    <circle cx="8" cy="12.5" r="1.3" />
  </svg>
);

export const GripIcon = () => (
  <svg viewBox="0 0 10 14" fill="currentColor" aria-hidden>
    <circle cx="3" cy="3" r="1.2" />
    <circle cx="7" cy="3" r="1.2" />
    <circle cx="3" cy="7" r="1.2" />
    <circle cx="7" cy="7" r="1.2" />
    <circle cx="3" cy="11" r="1.2" />
    <circle cx="7" cy="11" r="1.2" />
  </svg>
);

export const PlusIcon = () => (
  <svg {...line} strokeWidth="1.6">
    <path d="M8 3v10M3 8h10" />
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

export const BeatIcon = () => (
  <svg {...line} strokeWidth="1.5">
    <path d="M1.5 8.5h3l2-5 3 9 2-4h3" />
  </svg>
);

export const PlayIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <circle cx="8" cy="8" r="6" />
    <path d="M6.6 5.6v4.8L10.4 8z" fill="currentColor" />
  </svg>
);

export const ClearIcon = () => (
  <svg {...line} strokeWidth="1.5">
    <path d="M8 2v8M4.5 6.5 8 10l3.5-3.5M2.5 13.5h11" />
  </svg>
);

export const EyeIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <path d="M2 8s2.2-4.5 6-4.5S14 8 14 8s-2.2 4.5-6 4.5S2 8 2 8z" />
    <circle cx="8" cy="8" r="1.8" />
  </svg>
);

export const EyeOffIcon = () => (
  <svg {...line} strokeWidth="1.4">
    <path d="M2 8s2.2-4.5 6-4.5S14 8 14 8s-2.2 4.5-6 4.5S2 8 2 8z" />
    <path d="M2.5 13.5l11-11" />
  </svg>
);
