import type { ReactNode } from 'react';

// The 16px line icons of design/screens/codex.html (its `I` table), drawn in `currentColor`.
function Icon({
  children,
  size = 13,
  fill,
}: {
  children: ReactNode;
  size?: number;
  fill?: boolean;
}) {
  return (
    <svg
      viewBox="0 0 16 16"
      width={size}
      height={size}
      fill={fill ? 'currentColor' : 'none'}
      stroke={fill ? undefined : 'currentColor'}
      strokeWidth="1.6"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  );
}

export const PlusIcon = ({ size }: { size?: number }) => (
  <Icon size={size}>
    <path d="M8 3v10M3 8h10" />
  </Icon>
);
export const ChevronDownIcon = ({ size }: { size?: number }) => (
  <Icon size={size}>
    <path d="m4 6 4 4 4-4" />
  </Icon>
);
export const ArrowUpIcon = () => (
  <Icon>
    <path d="m4 10 4-4 4 4" />
  </Icon>
);
export const ArrowDownIcon = () => (
  <Icon>
    <path d="m4 6 4 4 4-4" />
  </Icon>
);
export const CloseIcon = () => (
  <Icon>
    <path d="m4 4 8 8M12 4l-8 8" />
  </Icon>
);
export const MoreIcon = () => (
  <Icon fill>
    <circle cx="3.5" cy="8" r="1.3" />
    <circle cx="8" cy="8" r="1.3" />
    <circle cx="12.5" cy="8" r="1.3" />
  </Icon>
);
export const ExpandIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path d="M9.5 2.5h4v4M6.5 13.5h-4v-4M13.5 2.5 9 7M2.5 13.5 7 9" />
  </svg>
);
export const CollapseIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path d="M13.5 6.5h-4v-4M2.5 9.5h4v4M9.5 6.5l4-4M6.5 9.5l-4 4" />
  </svg>
);
export const CopyIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.4"
    aria-hidden="true"
  >
    <rect x="5.5" y="5.5" width="8" height="8" rx="1.5" />
    <path d="M10.5 5.5v-2a1 1 0 0 0-1-1h-6a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2" />
  </svg>
);
export const QuoteIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path d="M3 3v10M6.5 5h7M6.5 8h7M6.5 11h4" />
  </svg>
);
export const ListIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path d="M6 4h7.5M6 8h7.5M6 12h7.5" />
    <circle cx="3" cy="4" r=".9" fill="currentColor" />
    <circle cx="3" cy="8" r=".9" fill="currentColor" />
    <circle cx="3" cy="12" r=".9" fill="currentColor" />
  </svg>
);
export const AtIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.4"
    aria-hidden="true"
  >
    <circle cx="8" cy="8" r="2.5" />
    <path d="M10.5 8v1.2a1.8 1.8 0 0 0 3.5 0V8a6 6 0 1 0-2.4 4.8" />
  </svg>
);
export const SearchIcon = () => (
  <Icon>
    <circle cx="7" cy="7" r="4.5" />
    <path d="m10.5 10.5 3 3" />
  </Icon>
);
export const FilterIcon = () => (
  <svg
    viewBox="0 0 16 16"
    width="13"
    height="13"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path d="M2 3.5h12L9.5 9v4l-3 1V9z" />
  </svg>
);
