import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import type { Marker } from '@codex/core/api/references';
import { useCanWrite } from '../../shell/bookAccess';
import { referencesLink } from '@codex/core/shell/links';
import { markerTone, reasonLabel, statusLabel } from './markerTone';
import { writeTestIds } from './testIds';
import { useCitations } from './useCitations';
import { useReturnFocus } from '../library/useReturnFocus';

interface MarkerCardProps {
  bookId: string;
  marker: Marker;
  anchor: HTMLElement;
  onClose: () => void;
}

// The card a source marker opens: the source, its clearance status and reason, the words quoted from it against the
// limit, a link to it in References and Remove (proposed: the words stay, the use is archived; a marker in a pending
// AI draft has none, since applying or discarding the draft settles it). Esc and a click outside close it; opened from
// the keyboard it takes focus, and Esc gives it back to the number.
export function MarkerCard({ bookId, marker, anchor, onClose }: MarkerCardProps) {
  // Below write (#80) the card reads; Remove is not offered.
  const canWrite = useCanWrite();
  useReturnFocus();
  const card = useRef<HTMLDivElement>(null);
  const citations = useCitations();
  const [removing, setRemoving] = useState(false);
  // Opened from the keyboard (the number has focus): move into the card so Tab reaches Open in References and Remove.
  useEffect(() => {
    if (document.activeElement === anchor) card.current?.focus();
  }, [anchor]);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Element;
      // Another marker's click replaces the card itself.
      if (card.current?.contains(target) || target.closest('[data-marker-use]')) return;
      onClose();
    };
    window.addEventListener('keydown', onKey);
    window.addEventListener('pointerdown', onPointer);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('pointerdown', onPointer);
    };
  }, [onClose]);

  const rect = anchor.getBoundingClientRect();
  const left = Math.max(8, Math.min(rect.left, window.innerWidth - 320));
  const tone = markerTone(marker.status);
  return (
    <div
      ref={card}
      role="dialog"
      aria-label={marker.sourceTitle}
      tabIndex={-1}
      data-testid={writeTestIds.markerCard}
      data-use={marker.useId}
      className="write-marker-card"
      style={{ left, top: rect.bottom + 6 }}
    >
      <span className="write-marker-kind">
        Reference {marker.number} · {marker.kind}
      </span>
      <h4 data-testid={writeTestIds.markerCardSource} className="write-marker-title">
        {marker.sourceTitle}
      </h4>
      <span
        data-testid={writeTestIds.markerCardStatus}
        data-tone={tone}
        className="write-marker-status"
      >
        <i />
        {statusLabel(marker.status)}
      </span>
      <p data-testid={writeTestIds.markerCardReason}>{reasonLabel(marker.reason)}</p>
      <p data-testid={writeTestIds.markerCardWords}>
        {marker.quotedWords} of {marker.limit} quoted words
      </p>
      <Link
        data-testid={writeTestIds.markerCardOpen}
        className="write-chip"
        to={referencesLink(bookId, { source: marker.sourceId })}
      >
        Open in References
      </Link>
      {canWrite && marker.generationId === null && (
        <button
          type="button"
          data-testid={writeTestIds.citeRemove}
          className="write-chip write-cite-remove"
          aria-label={`Remove reference ${marker.number}`}
          disabled={removing}
          onClick={() => {
            setRemoving(true);
            void citations.remove(marker).then((removed) => {
              setRemoving(false);
              if (removed) onClose();
            });
          }}
        >
          Remove
        </button>
      )}
    </div>
  );
}
