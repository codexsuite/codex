import { useState, type CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import type { SourceDetail, VerdictAction } from '@codex/core/api/referenceSources';
import { writeLink } from '@codex/core/shell/links';
import { writeReturnLink } from '../../write/citeReturn';
import { RefIcon, type RefIconName } from '../RefIcon';
import { lookOf } from '../sourceStatus';
import { refsTestIds } from '../testIds';
import { FairUseForm } from './FairUseForm';
import { paraphraseTarget, sceneOf } from './paraphraseTarget';
import { verdictNote, verdictReason } from './verdictWords';
import { useCanWrite } from '../../../shell/bookAccess';

const actionLabels: Partial<Record<VerdictAction, string>> = {
  sign_off: 'Mark as cleared',
  reopen: 'Reopen',
  accept_fair_use: 'Accept as fair use',
  set_licence: 'Set licence',
  fill_details: 'Fill in details',
  recheck: 'Re-check links',
  remove: 'Remove source',
};

const statusIcons: Record<string, RefIconName> = {
  cleared: 'shield',
  not_cited: 'flag',
};

// `.btn.sm`, `.btn.sm.ghost` and `.btn.sm.primary` of the reference screens.
export const smallButton =
  'inline-flex h-6 cursor-pointer items-center gap-[5px] rounded-sm border border-border bg-surface px-2 text-xs font-medium whitespace-nowrap text-text no-underline hover:bg-surface-2 disabled:cursor-default disabled:opacity-50';
export const smallGhost =
  'inline-flex h-6 cursor-pointer items-center gap-[5px] rounded-sm border border-transparent bg-transparent px-2 text-xs font-medium whitespace-nowrap text-text no-underline hover:bg-surface-2';
export const smallPrimary =
  'inline-flex h-6 cursor-pointer items-center gap-[5px] rounded-sm border border-accent bg-accent px-2 text-xs font-medium whitespace-nowrap text-accent-fg no-underline hover:brightness-[1.07] disabled:cursor-default disabled:opacity-50';

// The status, its reason and the next step, tinted with the status colour. Signing off, reopening, fair use,
// re-checking and removing are handled by the panel. A use that needs permission offers to ask for it (the tracker
// below follows the request) and, for quotes over the limit, to paraphrase one instead.
export function VerdictBlock({
  source,
  busy,
  onAct,
  onFairUse,
  onAskPermission,
}: {
  source: SourceDetail;
  busy: boolean;
  onAct: (action: VerdictAction) => void;
  onFairUse: (reason: string) => void;
  onAskPermission: () => void;
}) {
  const [askingFairUse, setAskingFairUse] = useState(false);
  // Below write (#80) the verdict reads; sign-off, fair use, permission and the Write detours are not offered.
  const canWrite = useCanWrite();
  const { verdict } = source;
  const look = lookOf(verdict.status);
  const action = verdict.action;
  const label = actionLabels[action];
  const signedOff = verdict.signedOffLabel
    ? `Signed off by ${verdict.signedOffBy ?? 'Codex'} · ${verdict.signedOffLabel}`
    : null;
  const clearAgain = action === 'sign_off' && verdict.reason === 'new_uses_since_signoff';
  const canAsk =
    action === 'permission' &&
    (source.permission === null || source.permission.status === 'refused');
  const paraphrase = action === 'permission' ? paraphraseTarget(source) : null;
  // A refused request leaves two ways out besides asking again: cut the passage or replace what it uses.
  const refusedUse = verdict.reason === 'permission_refused' ? source.uses[0] : undefined;
  const tone = { '--tone': look?.tone ?? 'var(--color-muted)' } as CSSProperties;
  return (
    <div
      data-testid={refsTestIds.verdict}
      data-status={verdict.status}
      style={tone}
      className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-0.5 rounded-md border border-[color-mix(in_srgb,var(--tone)_40%,var(--color-border))] bg-[color-mix(in_srgb,var(--tone)_8%,var(--color-surface))] px-2.5 py-[9px]"
    >
      <RefIcon
        name={statusIcons[verdict.status] ?? 'warn'}
        className="mt-px size-4 text-[var(--tone)]"
      />
      <b className="text-sm font-semibold text-[var(--tone)]">
        <span data-testid={refsTestIds.verdictStatus}>{look?.label ?? verdict.status}</span>
        {verdict.status !== 'not_cited' && (
          <>
            {' · '}
            <span data-testid={refsTestIds.verdictReason}>{verdictReason(source)}</span>
          </>
        )}
      </b>
      <p data-testid={refsTestIds.verdictNote} className="col-start-2 m-0 text-xs text-muted">
        {verdictNote(source)}
      </p>
      {signedOff && (
        <p data-testid={refsTestIds.signedOff} className="col-start-2 m-0 text-xs text-muted">
          {signedOff}
        </p>
      )}
      {canWrite &&
        (label !== undefined || canAsk || paraphrase !== null || refusedUse !== undefined) && (
          <div className="col-start-2 mt-1.5 flex flex-wrap gap-1.5">
            {label !== undefined && (
              <button
                type="button"
                disabled={busy}
                data-testid={refsTestIds.verdictAction}
                data-action={action}
                onClick={() =>
                  action === 'accept_fair_use' ? setAskingFairUse(true) : onAct(action)
                }
                className={
                  action === 'reopen'
                    ? smallGhost
                    : action === 'remove'
                      ? smallButton
                      : smallPrimary
                }
              >
                {(action === 'sign_off' || action === 'accept_fair_use') && (
                  <RefIcon name={action === 'sign_off' ? 'shield' : 'check'} />
                )}
                {clearAgain ? 'Clear again' : label}
              </button>
            )}
            {canAsk && (
              <button
                type="button"
                disabled={busy}
                data-testid={refsTestIds.verdictAsk}
                onClick={onAskPermission}
                className={smallPrimary}
              >
                <RefIcon name="mail" />
                Ask for permission
              </button>
            )}
            {refusedUse !== undefined && (
              <Link
                to={writeLink(source.bookId, { scene: sceneOf(refusedUse), ref: source.id })}
                data-testid={refsTestIds.verdictCut}
                title="Opens the passage in Write with its marker card open"
                className={smallPrimary}
              >
                {refusedUse.kind === 'figure'
                  ? 'Cut or replace the figure'
                  : 'Cut or replace the passage'}
              </Link>
            )}
            {paraphrase !== null && (
              <Link
                to={writeReturnLink(source.bookId, {
                  scene: paraphrase.scene,
                  ref: source.id,
                  paraphrase: true,
                })}
                data-testid={refsTestIds.verdictParaphrase}
                title={`Opens ${paraphrase.where} in Write with a Scene beat that rewrites the quote`}
                className={smallButton}
              >
                <RefIcon name="spark" />
                Paraphrase instead
              </Link>
            )}
          </div>
        )}
      {askingFairUse && action === 'accept_fair_use' && (
        <div className="col-start-2">
          <FairUseForm busy={busy} onAccept={onFairUse} />
        </div>
      )}
    </div>
  );
}
