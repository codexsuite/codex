import { useCanWrite } from '../../shell/bookAccess';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { createPortal } from 'react-dom';
import { placementLabels, type Placement } from '@codex/core/api/assets';
import { FigureDialog } from './FigureDialog';
import {
  figureApi,
  figureKey,
  useFigure,
  type Figure,
  type FigureFields,
  type PatchFigureRequest,
} from './figureApi';
import { writeTestIds } from './testIds';

interface FigureCardProps {
  figureId: string;
  bookId: string;
  /** Takes the figure's line out of the scene (with Undo in the toast). */
  onRemove: () => void;
}

const placementLabel = (placement: string) => placementLabels[placement as Placement] ?? placement;

/** Only what the dialog changed; an emptied caption or alt text is sent as '' to clear it. */
function changes(figure: Figure, fields: FigureFields): PatchFigureRequest {
  const patch: PatchFigureRequest = {};
  if (fields.assetId !== figure.assetId) patch.assetId = fields.assetId;
  if (fields.placement !== figure.placement) patch.placement = fields.placement;
  if (fields.caption.trim() !== (figure.caption ?? '')) patch.caption = fields.caption.trim();
  if (fields.altText.trim() !== (figure.altText ?? '')) patch.altText = fields.altText.trim();
  return patch;
}

function CardActions({
  figureId,
  onEdit,
  onRemove,
}: {
  figureId: string;
  onEdit: () => void;
  onRemove: () => void;
}) {
  // Below write (#80) a figure reads; Edit and Remove would be refused.
  if (!useCanWrite()) return null;
  return (
    <>
      <button
        type="button"
        data-testid={writeTestIds.figureEdit(figureId)}
        title="Change the image, placement, caption or alt text"
        onClick={onEdit}
        className="write-mini"
      >
        Edit
      </button>
      <button
        type="button"
        data-testid={writeTestIds.figureRemove(figureId)}
        title="Take the figure out of the scene"
        onClick={onRemove}
        className="write-mini"
      >
        Remove
      </button>
    </>
  );
}

// The card drawn for a `::figure` line: the image (the asset's web rendition), where it sits, its caption, and a
// badge when the image is too small for the book's trim. Edit changes the image, placement, caption and alt text
// (the row only, so the line round-trips unchanged); Remove takes the line out of the scene.
export function FigureCard({ figureId, bookId, onRemove }: FigureCardProps) {
  const figure = useFigure(figureId);
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);

  if (figure.isError) {
    return (
      <div data-testid={writeTestIds.figureMissing(figureId)} className="text-sm text-muted">
        <span className="prose-card-label">Figure</span> This figure could not be found.
      </div>
    );
  }
  if (!figure.data) return <span className="prose-card-label">Figure</span>;

  const current = figure.data;
  const { missing, webUrl, caption, altText, dpi, placement } = current;

  const save = async (fields: FigureFields): Promise<string | null> => {
    const patch = changes(current, fields);
    if (Object.keys(patch).length === 0) {
      setEditing(false);
      return null;
    }
    try {
      const saved = await figureApi.patch(figureId, patch);
      queryClient.setQueryData(figureKey(figureId), saved);
      setEditing(false);
      return null;
    } catch (e) {
      return e instanceof Error ? e.message : 'The figure could not be saved.';
    }
  };

  // Portalled out of the editor: the card is a ProseMirror node view, and the dialog's fields are not prose.
  const dialog =
    editing &&
    createPortal(
      <FigureDialog
        bookId={bookId}
        mode="edit"
        initial={{
          assetId: current.assetId,
          placement: current.placement as Placement,
          caption: caption ?? '',
          altText: altText ?? '',
        }}
        onClose={() => setEditing(false)}
        onSubmit={save}
      />,
      document.body,
    );
  const actions = (
    <CardActions figureId={figureId} onEdit={() => setEditing(true)} onRemove={onRemove} />
  );

  if (missing) {
    return (
      <div
        data-testid={writeTestIds.figureMissing(figureId)}
        className="flex flex-col gap-1 rounded-md border border-dashed border-border p-3 text-sm"
      >
        <div className="flex items-center gap-2">
          <span className="prose-card-label">Figure</span>
          <span className="flex-1" />
          {actions}
        </div>
        <span className="text-muted">
          The image for this figure is missing. Edit it to choose another, or remove the figure.
        </span>
        {caption && <span className="italic">{caption}</span>}
        {dialog}
      </div>
    );
  }

  return (
    <figure data-testid={writeTestIds.figure(figureId)} className="m-0 flex flex-col gap-1.5">
      <div className="flex items-center gap-2">
        <span className="prose-card-label">Figure</span>
        <span
          data-testid={writeTestIds.figurePlacementLabel(figureId)}
          className="text-xs text-muted"
        >
          {placementLabel(placement)}
        </span>
        <span className="flex-1" />
        {dpi && (
          <span
            data-testid={
              dpi.status === 'low'
                ? writeTestIds.figureLowDpi(figureId)
                : writeTestIds.figureDpi(figureId)
            }
            data-status={dpi.status}
            title={`Effective resolution where the figure is placed; the target is ${dpi.targetDpi} dpi.`}
            className={`rounded-sm px-1.5 text-xs ${
              dpi.status === 'low' ? 'bg-surface-2 font-semibold text-warn' : 'text-muted'
            }`}
          >
            {dpi.status === 'low'
              ? `Low resolution · ${Math.round(dpi.effectiveDpi)} dpi`
              : `${Math.round(dpi.effectiveDpi)} dpi`}
          </span>
        )}
        {actions}
      </div>
      {webUrl ? (
        <img
          data-testid={writeTestIds.figureImage(figureId)}
          src={webUrl}
          alt={altText ?? caption ?? 'Figure'}
          className="max-h-80 max-w-full self-start rounded-sm"
        />
      ) : (
        <div data-testid={writeTestIds.figurePending(figureId)} className="text-sm text-muted">
          The image is still being prepared.
        </div>
      )}
      {caption && (
        <figcaption data-testid={writeTestIds.figureCaption(figureId)} className="text-sm italic">
          {caption}
        </figcaption>
      )}
      {altText && (
        <span data-testid={writeTestIds.figureAlt(figureId)} className="text-xs text-muted">
          Alt text: {altText}
        </span>
      )}
      {dialog}
    </figure>
  );
}
