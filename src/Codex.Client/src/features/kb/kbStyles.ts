// Class strings shared by the entry window's parts; each mirrors a rule in design/screens/codex.html.

/** `.ib`: a 22px icon button. */
export const iconButton =
  'inline-grid h-[22px] w-[22px] place-items-center rounded-xs border-0 bg-transparent p-0 text-muted hover:bg-surface-2 hover:text-text disabled:opacity-35 disabled:hover:bg-transparent disabled:hover:text-muted';

/** `.mini`: a small text button, optionally with a leading icon. */
export const miniButton =
  'inline-flex h-[22px] items-center gap-1 rounded-xs border-0 bg-transparent px-1.5 text-xs font-medium whitespace-nowrap text-muted hover:bg-surface-2 hover:text-text';

/** `.kb-sum`, `.fv`, `.kb-name`: text that reads as plain text, tints on hover and rings on focus. */
export const inlineField =
  'border-0 bg-transparent outline-none hover:bg-[color-mix(in_srgb,var(--color-surface-2)_60%,transparent)] focus:bg-surface focus:shadow-[0_0_0_2px_var(--color-accent)]';

/** `.rail-h`: a rail section heading. */
export const railHeading =
  'm-0 flex min-h-[22px] items-center gap-1.5 text-2xs font-semibold tracking-[.04em] text-muted uppercase';

/** `.catsel` / `.cx` tint: the entry's category colour at 14%. `--cat` must be set. */
export const categoryTint = 'bg-[color-mix(in_srgb,var(--cat)_14%,transparent)]';

/** `.sec-h .overline`: the Summary and Content labels. */
export const overline = 'text-2xs font-semibold tracking-[.02em] text-muted uppercase';
