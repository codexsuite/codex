import { Fragment, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  attachPermissionLetter,
  draftPermission,
  grantPermission,
  markPermissionFollowedUp,
  markPermissionSent,
  patchPermission,
  refusePermission,
  removePermissionLetter,
  withdrawPermission,
  type Permission,
  type SourceDetail,
} from '@codex/core/api/referenceSources';
import { showToast } from '@codex/core/editing/toastStore';
import { writeLink } from '@codex/core/shell/links';
import { RefIcon } from '../RefIcon';
import { refsTestIds } from '../testIds';
import { GrantForm } from './GrantForm';
import { PermissionDraft } from './PermissionDraft';
import { sceneOf } from './paraphraseTarget';
import { RefuseForm } from './RefuseForm';
import { smallButton, smallGhost, smallPrimary } from './VerdictBlock';

const stepOf = { draft: 0, sent: 1, granted: 2, refused: 2 } as const;

const button = 'rounded-sm border border-border px-3 py-1 text-sm text-text disabled:opacity-50';

// Drafted, Sent, then Granted or Refused. The letter is drafted by the server from the uses and the book's letter
// settings; every action answers with the refreshed panel.
export function PermissionTracker({
  source,
  busy,
  run,
  fail,
}: {
  source: SourceDetail;
  busy: boolean;
  run: (write: () => Promise<SourceDetail>, quiet?: boolean) => Promise<boolean>;
  fail: (message: string) => void;
}) {
  const permission = source.permission;
  const [asking, setAsking] = useState<'granted' | 'refused' | null>(null);
  const waiting = permission?.status === 'draft' || permission?.status === 'sent';
  const canDraft = permission === null || permission.status === 'refused';
  const reached = permission === null ? -1 : stepOf[permission.status];
  const steps = ['Drafted', 'Sent', permission?.status === 'refused' ? 'Refused' : 'Granted'];
  const firstUse = source.uses[0];

  const copy = (text: string, done = 'Letter copied') => {
    navigator.clipboard.writeText(text).then(
      () => showToast({ message: done }),
      () => fail('The text could not be copied.'),
    );
  };

  return (
    <div
      data-testid={refsTestIds.permission}
      data-status={permission?.status ?? 'none'}
      className="mt-2 flex flex-col gap-1.5 rounded-md border border-border bg-bg px-2.5 py-2"
    >
      <ol className="m-0 flex list-none items-center gap-1 p-0 text-2xs font-medium whitespace-nowrap text-muted">
        {steps.map((step, index) => {
          const refused = permission?.status === 'refused' && index === 2;
          const on = index <= reached;
          return (
            <Fragment key={step}>
              {index > 0 && <li aria-hidden className="h-px min-w-2 flex-1 bg-border" />}
              <li
                className={`inline-flex items-center gap-1 ${refused ? 'text-danger' : on ? 'text-text' : ''}`}
              >
                <i
                  className={`size-2 rounded-full ${
                    refused
                      ? 'bg-danger'
                      : on
                        ? 'bg-accent'
                        : 'shadow-[inset_0_0_0_1.5px_currentColor]'
                  }`}
                />
                {step}
              </li>
            </Fragment>
          );
        })}
      </ol>
      {permission === null && (
        <p className="m-0 text-xs text-muted">
          No request yet. Codex drafts the letter from the uses below: what you quote, where, and
          the print run.
        </p>
      )}
      {permission !== null && (
        <Summary
          permission={permission}
          busy={busy}
          onRemoveLetter={() => void run(() => removePermissionLetter(permission.id))}
        />
      )}
      {permission?.status === 'granted' && permission.attachment === null && (
        <label className="flex flex-col gap-1 text-sm text-muted">
          Attach the signed letter
          <input
            type="file"
            accept=".pdf,.png,.jpg,.jpeg,.txt,.doc,.docx,.eml"
            disabled={busy}
            data-testid={refsTestIds.permAttachInput}
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) void run(() => attachPermissionLetter(permission.id, file));
            }}
            className="text-sm text-text"
          />
        </label>
      )}
      {canDraft && (
        <div className="flex flex-wrap gap-1.5">
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.permDraft}
            onClick={() => void run(() => draftPermission(source.id))}
            className={smallPrimary}
          >
            <RefIcon name="mail" />
            {permission === null ? 'Draft request' : 'Draft a new request'}
          </button>
          {firstUse !== undefined && (
            <Link
              to={writeLink(source.bookId, { scene: sceneOf(firstUse), ref: source.id })}
              data-testid={refsTestIds.permRemoveUses}
              title="Opens the first passage in Write so you can cut it"
              className={smallGhost}
            >
              Remove the uses instead
            </Link>
          )}
        </div>
      )}
      {permission?.status === 'draft' && (
        <PermissionDraft
          key={permission.id}
          permission={permission}
          busy={busy}
          onSave={(body, to) =>
            run(() => patchPermission(permission.id, { body, to, remindOn: null }), true)
          }
          onSend={(body, to) =>
            void run(async () => {
              if (body !== null || to !== null) {
                await patchPermission(permission.id, { body, to, remindOn: null });
              }
              return markPermissionSent(permission.id);
            })
          }
          onCopy={copy}
          onWithdraw={() => void run(() => withdrawPermission(permission.id))}
        />
      )}
      {permission?.status === 'sent' && (
        <>
          <div className="flex flex-wrap items-center gap-1.5 text-sm text-muted">
            Remind me again in
            {[3, 7].map((days) => (
              <button
                key={days}
                type="button"
                disabled={busy}
                data-testid={refsTestIds.permRemindIn(days)}
                onClick={() =>
                  void run(() =>
                    patchPermission(permission.id, {
                      body: null,
                      to: null,
                      remindOn: null,
                      remindInDays: days,
                    }),
                  )
                }
                className={smallButton}
              >
                {days} days
              </button>
            ))}
          </div>
          {permission.followUp !== null && (
            <div className="flex flex-wrap items-center gap-1.5">
              <button
                type="button"
                disabled={busy}
                data-testid={refsTestIds.permCopyFollowUp}
                onClick={() => copy(permission.followUp ?? '', 'Follow-up copied')}
                className={smallButton}
              >
                <RefIcon name="mail" />
                Copy follow-up
              </button>
              <button
                type="button"
                disabled={busy}
                data-testid={refsTestIds.permFollowedUp}
                onClick={() => void run(() => markPermissionFollowedUp(permission.id))}
                className={smallButton}
              >
                Mark followed up
              </button>
            </div>
          )}
          <label className="flex items-center gap-2 text-sm text-muted">
            Remind me on
            <input
              type="date"
              value={permission.remindOn ?? ''}
              disabled={busy}
              data-testid={refsTestIds.permRemind}
              onChange={(event) =>
                void run(() =>
                  patchPermission(permission.id, {
                    body: null,
                    to: null,
                    remindOn: event.target.value,
                  }),
                )
              }
              className="rounded-sm border border-border bg-surface p-1 text-sm text-text"
            />
          </label>
          <details>
            <summary className="cursor-pointer text-sm text-muted">The letter as sent</summary>
            <pre
              data-testid={refsTestIds.permLetter}
              className="m-0 whitespace-pre-wrap font-sans text-sm text-text"
            >
              {permission.letter}
            </pre>
          </details>
        </>
      )}
      {permission?.status === 'sent' && asking === null && (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.permGranted}
            onClick={() => setAsking('granted')}
            className={button}
          >
            Granted
          </button>
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.permRefused}
            onClick={() => setAsking('refused')}
            className={button}
          >
            Refused
          </button>
          <button
            type="button"
            disabled={busy}
            data-testid={refsTestIds.permWithdraw}
            onClick={() => void run(() => withdrawPermission(permission.id))}
            className={`${button} text-muted`}
          >
            Withdraw
          </button>
        </div>
      )}
      {permission?.status === 'sent' && asking === 'granted' && (
        <GrantForm
          busy={busy}
          onGrant={(letter, creditLine, terms, fee) =>
            void run(() => grantPermission(permission.id, { letter, creditLine, terms, fee }))
          }
          onCancel={() => setAsking(null)}
        />
      )}
      {permission?.status === 'sent' && asking === 'refused' && (
        <RefuseForm
          busy={busy}
          onRefuse={(note) => void run(() => refusePermission(permission.id, note))}
        />
      )}
      {waiting && asking !== null && (
        <div>
          <button
            type="button"
            className="text-xs text-muted underline"
            onClick={() => setAsking(null)}
          >
            Back
          </button>
        </div>
      )}
    </div>
  );
}

// What the request stands at, in the server's words.
function Summary({
  permission,
  busy,
  onRemoveLetter,
}: {
  permission: Permission;
  busy: boolean;
  onRemoveLetter: () => void;
}) {
  const [removing, setRemoving] = useState(false);
  return (
    <div className="flex flex-col gap-1 text-sm">
      <b data-testid={refsTestIds.permStatus} className="text-text">
        {statusWords[permission.status]}
      </b>
      {permission.sentLabel !== null && (
        <span data-testid={refsTestIds.permSentLabel} className="text-muted">
          {permission.sentLabel}
          {permission.status === 'sent' && ' · no reply'}
        </span>
      )}
      {permission.remindLabel !== null && (
        <span
          data-testid={refsTestIds.permRemindLabel}
          className={permission.remindDue ? 'text-warn' : 'text-muted'}
        >
          {permission.remindLabel}
        </span>
      )}
      {permission.followedUpLabel !== null && (
        <span data-testid={refsTestIds.permFollowedUpLabel} className="text-muted">
          {permission.followedUpLabel}
        </span>
      )}
      {permission.respondedLabel !== null && (
        <span data-testid={refsTestIds.permRespondedLabel} className="text-muted">
          {permission.respondedLabel}
        </span>
      )}
      {permission.creditLine !== null && (
        <span data-testid={refsTestIds.permCredit} className="text-text">
          Credit line: {permission.creditLine}
        </span>
      )}
      {permission.status === 'refused' && permission.note !== null && (
        <span data-testid={refsTestIds.permNote} className="text-muted">
          {permission.note}
        </span>
      )}
      {permission.attachment !== null && (
        <span className="flex items-center gap-2">
          <a
            data-testid={refsTestIds.permAttachment}
            href={permission.attachment.url}
            className="text-accent underline"
          >
            {permission.attachment.fileName}
          </a>
          {permission.status === 'granted' && !removing && (
            <button
              type="button"
              aria-label="Remove the letter"
              disabled={busy}
              data-testid={refsTestIds.permAttachmentRemove}
              onClick={() => setRemoving(true)}
              className="cursor-pointer border-0 bg-transparent text-muted hover:text-danger"
            >
              ✕
            </button>
          )}
          {removing && (
            <button
              type="button"
              disabled={busy}
              data-testid={refsTestIds.permAttachmentConfirm}
              onClick={() => {
                setRemoving(false);
                onRemoveLetter();
              }}
              className="rounded-sm border border-danger px-2 text-xs text-danger"
            >
              Remove this letter
            </button>
          )}
        </span>
      )}
    </div>
  );
}

const statusWords = {
  draft: 'Draft request',
  sent: 'Permission asked',
  granted: 'Permission granted',
  refused: 'Permission refused',
} as const;
