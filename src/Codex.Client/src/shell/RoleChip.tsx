import { showToast } from '@codex/core/editing/toastStore';
import { testIds } from '../testIds';
import { useBookAccess } from './bookAccess';

// The bookbar's quiet chip below write (#80, design/screens/write.html): why typing does nothing, in the #26 F-06
// style. Its tooltip (and a click, for touch) names the owner. Nothing for write or owner.
export function RoleChip() {
  const { role, ownerName } = useBookAccess();
  if (role !== 'view' && role !== 'comment') return null;
  const label = role === 'view' ? 'View only' : 'Comment only';
  const why =
    role === 'view'
      ? `${ownerName} shared this book with you to read. Every tab opens, but typing and changes are turned off.`
      : `You can read this book and comment on it, but not change the text. Ask ${ownerName} for edit access.`;
  return (
    <button
      type="button"
      data-testid={testIds.shell.roleChip}
      title={why}
      aria-label={`${label}. ${why}`}
      onClick={() => showToast({ message: why })}
      className="inline-flex h-[22px] flex-none cursor-help items-center gap-[5px] rounded-[11px] border-0 bg-surface-2 px-2 text-2xs font-medium whitespace-nowrap text-muted focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent [&_svg]:size-[11px]"
    >
      {role === 'view' ? (
        <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden>
          <path d="M1.5 8S4 3.5 8 3.5 14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8z" />
          <circle cx="8" cy="8" r="2" />
        </svg>
      ) : (
        <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden>
          <path d="M2.5 3.5h11v7h-6l-3 2.5v-2.5h-2z" />
        </svg>
      )}
      <span className="max-[720px]:hidden">{label}</span>
    </button>
  );
}
