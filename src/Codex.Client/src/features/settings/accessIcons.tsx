// Line icons for Sharing and the read-only states, drawn as in design/screens/settings.html. Sized by the caller.
const line = {
  viewBox: '0 0 16 16',
  fill: 'none',
  stroke: 'currentColor',
  'aria-hidden': true,
} as const;

export const EyeIcon = ({ className }: { className?: string }) => (
  <svg {...line} strokeWidth="1.5" className={className}>
    <path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8z" />
    <circle cx="8" cy="8" r="2" />
  </svg>
);

export const CommentIcon = ({ className }: { className?: string }) => (
  <svg {...line} strokeWidth="1.5" className={className}>
    <path d="M2.5 3.5h11v7h-6l-3 2.5v-2.5h-2z" />
  </svg>
);

export const LockIcon = ({ className }: { className?: string }) => (
  <svg {...line} strokeWidth="1.6" className={className}>
    <rect x="3.5" y="7" width="9" height="6.5" rx="1" />
    <path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2" />
  </svg>
);

export const InfoIcon = ({ className }: { className?: string }) => (
  <svg {...line} strokeWidth="1.6" className={className}>
    <circle cx="8" cy="8" r="6" />
    <path d="M8 7.2v4M8 4.8v.3" />
  </svg>
);

/** Path data for the Sharing nav entry and the people lines (16×16, stroked). */
export const peoplePath =
  'M8.3 5.5a2.3 2.3 0 1 1-4.6 0 2.3 2.3 0 0 1 4.6 0zM1.8 13.5c.4-2.3 2.1-3.6 4.2-3.6s3.8 1.3 4.2 3.6M10.5 3.4a2.3 2.3 0 0 1 0 4.3M12 9.9c1.2.4 2 1.6 2.2 3.6';

export const PeopleIcon = ({ className }: { className?: string }) => (
  <svg {...line} strokeWidth="1.5" className={className}>
    <path d={peoplePath} />
  </svg>
);
