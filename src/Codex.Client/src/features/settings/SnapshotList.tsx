import type { Snapshot } from '@codex/core/api/settings';
import { settingsTestIds } from './testIds';

function stateText(snapshot: Snapshot) {
  if (snapshot.waiting) return 'Waiting for the running export';
  if (snapshot.state === 'queued' || snapshot.state === 'running') return 'Building…';
  if (snapshot.state === 'failed') return 'Failed';
  if (snapshot.state === 'cancelled') return 'Cancelled';
  return null;
}

// What was published, newest first: label, day, words and the .zip once it is built.
export function SnapshotList({ snapshots }: { snapshots: Snapshot[] }) {
  return (
    <div data-testid={settingsTestIds.snapshots} className="py-3">
      <b data-testid={settingsTestIds.snapshotsHeading} className="text-base text-text">
        Snapshots ({snapshots.length})
      </b>
      <ul className="m-0 mt-1 flex list-none flex-col gap-1 p-0 text-sm text-muted">
        {snapshots.map((snapshot) => {
          const note = stateText(snapshot);
          return (
            <li
              key={snapshot.id}
              data-testid={settingsTestIds.snapshot(snapshot.id)}
              className="flex flex-wrap items-center gap-2"
            >
              <span>
                {snapshot.label} · {snapshot.whenLabel} · {snapshot.words.toLocaleString('en-US')}{' '}
                words
              </span>
              {note && <span>{note}</span>}
              {snapshot.fileUrl && (
                <a
                  href={snapshot.fileUrl}
                  download
                  data-testid={settingsTestIds.snapshotDownload(snapshot.id)}
                  className="text-accent"
                >
                  Download
                </a>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
