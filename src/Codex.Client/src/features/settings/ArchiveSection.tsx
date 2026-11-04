import { useQueryClient } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { exportsKey, type ManuscriptFormat } from '@codex/core/api/exports';
import { bookKey } from '@codex/core/api/books';
import { settingsApi, settingsKey, type BookSettings } from '@codex/core/api/settings';
import { usePublishState } from '../../api/settingsQueries';
import { showToast } from '@codex/core/editing/toastStore';
import { planLink } from '@codex/core/shell/links';
import { ArchiveDialog } from './ArchiveDialog';
import { DuplicateDialog } from './DuplicateDialog';
import { ExportManuscriptDialog } from './ExportManuscriptDialog';
import { ManuscriptJobs } from './ManuscriptJobs';
import { SnapshotList } from './SnapshotList';
import { SettingsCard } from './SettingsCard';
import { useDocumentExports } from './useDocumentExports';
import { settingsTestIds } from './testIds';
import { useSharing } from '../../api/sharingQueries';
import { plural } from '@codex/core/features/settings/sharingRules';
import { useSettingsAccess } from './useSettingsAccess';

const button =
  'h-8 shrink-0 cursor-pointer rounded border border-border bg-surface px-3 text-base text-text disabled:cursor-not-allowed disabled:opacity-50';

function ActionRow({
  title,
  children,
  action,
}: {
  title: string;
  children: ReactNode;
  action: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-dashed border-border py-3 last:border-b-0">
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <b className="text-base text-text">{title}</b>
        <span className="text-sm text-muted">{children}</span>
      </div>
      {action}
    </div>
  );
}

// Duplicate (References excluded), Export source (queues a job), and Archive / Restore.
export function ArchiveSection({ settings }: { settings: BookSettings }) {
  const queryClient = useQueryClient();
  const [dialog, setDialog] = useState<'duplicate' | 'edition' | 'archive' | 'manuscript' | null>(
    null,
  );
  const [manuscriptFormat, setManuscriptFormat] = useState<ManuscriptFormat>('docx');
  const exportsNow = useDocumentExports(settings.id);
  const sourceRunning =
    exportsNow.source?.status === 'queued' || exportsNow.source?.status === 'running';
  const [copy, setCopy] = useState<{ id: string; title: string } | null>(null);
  const [exportState, setExportState] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Duplicate, new edition, archive and restore are the owner's (#80); exporting needs write.
  const { isOwner, canEdit, ownerName } = useSettingsAccess(settings.id);
  const sharing = useSharing(settings.id).data;
  const directGrants = isOwner ? (sharing?.book ?? []) : [];
  const ownerOnly = (what: string) =>
    isOwner ? undefined : `Only the owner can ${what} this book`;
  const publishState = usePublishState(settings.id);
  const snapshots = publishState.data?.snapshots ?? [];

  const run = async (work: () => Promise<void>, fallback: string) => {
    setBusy(true);
    setError(null);
    try {
      await work();
    } catch (e) {
      setError(e instanceof Error ? e.message : fallback);
    } finally {
      setBusy(false);
    }
  };

  // The newest source job's state once the list has it, else what the click just queued.
  const sourceStatus = exportsNow.source ? exportsNow.source.status : exportState;

  const publish = async (next: BookSettings) => {
    queryClient.setQueryData(settingsKey(settings.id), next);
    await queryClient.invalidateQueries({ queryKey: bookKey(settings.id) });
    await queryClient.invalidateQueries({ queryKey: ['library'] });
  };

  const archive = () =>
    run(async () => {
      await publish(await settingsApi.archive(settings.id));
      setDialog(null);
      showToast({
        message: `${settings.title} archived.`,
        undo: async () => publish(await settingsApi.restore(settings.id)),
      });
    }, 'The book could not be archived.');

  const restore = () =>
    run(
      async () => publish(await settingsApi.restore(settings.id)),
      'The book could not be restored.',
    );

  const exportSource = () =>
    run(async () => {
      const job = await settingsApi.exportSource(settings.id);
      setExportState(job.state);
      void queryClient.invalidateQueries({ queryKey: exportsKey(settings.id) });
      showToast({ message: 'Source export queued.' });
    }, 'The export could not be queued.');

  return (
    <SettingsCard
      section="archive"
      title="Archive & export"
      blurb={`Copies and backups of the whole book, and taking it off the shelf.${
        isOwner
          ? ''
          : ` Only ${ownerName}, the owner, can duplicate or archive it${canEdit ? '' : ', and exporting needs edit access'}.`
      }`}
    >
      {settings.archived && (
        <div
          data-testid={settingsTestIds.archivedBar}
          role="status"
          className="my-3 flex flex-wrap items-center justify-between gap-2 rounded border border-warn bg-bg p-2 text-sm text-warn"
        >
          <span>
            This book is archived: hidden from Library and the palette. Nothing was deleted.
          </span>
          <button
            type="button"
            data-testid={settingsTestIds.restore}
            disabled={busy || !isOwner}
            title={ownerOnly('restore')}
            onClick={() => void restore()}
            className={button}
          >
            Restore
          </button>
        </div>
      )}
      {error && (
        <p
          data-testid={settingsTestIds.lifecycleError}
          role="alert"
          className="m-0 py-2 text-sm text-danger"
        >
          {error}
        </p>
      )}
      <ActionRow
        title="Duplicate book"
        action={
          <button
            type="button"
            data-testid={settingsTestIds.duplicate}
            disabled={!isOwner}
            title={ownerOnly('duplicate')}
            onClick={() => setDialog('duplicate')}
            className={button}
          >
            Duplicate…
          </button>
        }
      >
        A new book with this one’s acts, chapters, scenes, prose, beats, Knowledge base and labels.
        References and their sign-offs aren’t copied, because clearance covers one book’s print run.
        {isOwner && ' People you shared this book with don’t get the copy.'}
        {copy && (
          <span data-testid={settingsTestIds.duplicateDone} className="mt-1 block text-text">
            Created{' '}
            <Link to={planLink(copy.id)} className="text-accent">
              {copy.title}
            </Link>
            .
          </span>
        )}
      </ActionRow>
      <ActionRow
        title="New edition"
        action={
          <button
            type="button"
            data-testid={settingsTestIds.newEdition}
            disabled={!isOwner}
            title={
              isOwner
                ? undefined
                : `Only ${ownerName}, the owner, can publish this book or start a new edition`
            }
            onClick={() => setDialog('edition')}
            className={button}
          >
            Start a new edition…
          </button>
        }
      >
        Like Duplicate, but the sources come along with their uses. Sign-offs and permission
        requests start clear, so the new edition is cleared on its own.
      </ActionRow>
      {snapshots.length > 0 && <SnapshotList snapshots={snapshots} />}
      <ActionRow
        title="Export source"
        action={
          <button
            type="button"
            data-testid={settingsTestIds.exportSource}
            disabled={busy || sourceRunning}
            onClick={() => void exportSource()}
            className={button}
          >
            Export .zip
          </button>
        }
      >
        A .zip of Markdown, one file per scene with its beats as directives, plus the Knowledge
        base, References and labels as JSON. Codex can import it again from Library → Import.
        Pending AI drafts and images are not included.
        {sourceStatus && (
          <span data-testid={settingsTestIds.exportStatus} className="mt-1 block text-text">
            Export {sourceStatus}.
            {exportsNow.source?.fileUrl && (
              <>
                {' '}
                <a
                  data-testid={settingsTestIds.exportDownload}
                  href={exportsNow.source.fileUrl}
                  download={exportsNow.source.filename ?? undefined}
                  className="text-accent"
                >
                  Download
                </a>
              </>
            )}
          </span>
        )}
        {exportsNow.source?.status === 'failed' && (
          <span className="mt-1 block text-danger">{exportsNow.source.error}</span>
        )}
        {exportsNow.source?.notes && exportsNow.source.notes.length > 0 && (
          <span data-testid={settingsTestIds.exportNotes} className="mt-1 block">
            {exportsNow.source.notes.join(' · ')}
          </span>
        )}
      </ActionRow>
      <ActionRow
        title="Export manuscript"
        action={
          <button
            type="button"
            data-testid={settingsTestIds.exportManuscript}
            onClick={() => {
              setManuscriptFormat('docx');
              setDialog('manuscript');
            }}
            className={button}
          >
            Export manuscript…
          </button>
        }
      >
        A Word, Markdown or EPUB copy for an editor or beta readers: not for print. Choose the whole
        book or one chapter, and whether to include beats and summaries.
        <ManuscriptJobs
          jobs={exportsNow.manuscripts}
          onRetry={(format) => {
            setManuscriptFormat(format);
            setDialog('manuscript');
          }}
        />
      </ActionRow>
      {!settings.archived && (
        <ActionRow
          title="Archive book"
          action={
            <button
              type="button"
              data-testid={settingsTestIds.archive}
              disabled={!isOwner}
              title={ownerOnly('archive')}
              onClick={() => setDialog('archive')}
              className={`${button} border-danger text-danger`}
            >
              Archive…
            </button>
          }
        >
          Hides it from Library and the palette. Nothing is deleted
          {settings.seriesName
            ? `, and it keeps its place in ${settings.seriesName} as an archived book`
            : ''}
          . Restore it from Library’s Archived list.
          {directGrants.length > 0 && (
            <span data-testid={settingsTestIds.archiveLoss}>
              {' '}
              {plural(directGrants.length, 'person', 'people')} you shared it with directly{' '}
              {directGrants.length === 1 ? 'loses' : 'lose'} access, and Restore doesn’t share it
              again.
            </span>
          )}
        </ActionRow>
      )}
      {(dialog === 'duplicate' || dialog === 'edition') && (
        <DuplicateDialog
          edition={dialog === 'edition'}
          settings={settings}
          onClose={() => setDialog(null)}
          onDuplicated={(created) => {
            setCopy(created);
            setDialog(null);
          }}
        />
      )}
      {dialog === 'manuscript' && (
        <ExportManuscriptDialog
          bookId={settings.id}
          initialFormat={manuscriptFormat}
          busyFormats={exportsNow.manuscripts
            .filter((job) => job.status === 'queued' || job.status === 'running')
            .map((job) => job.kind as ManuscriptFormat)}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'archive' && (
        <ArchiveDialog
          settings={settings}
          losing={directGrants.map((grant) => grant.user.displayName)}
          busy={busy}
          onClose={() => setDialog(null)}
          onConfirm={() => void archive()}
        />
      )}
    </SettingsCard>
  );
}
