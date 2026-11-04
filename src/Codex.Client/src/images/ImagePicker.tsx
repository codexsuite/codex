import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import {
  assetApi,
  bookImagesKey,
  placementLabels,
  webRendition,
  type Asset,
  type AssetDpi,
  type Placement,
} from '@codex/core/api/assets';
import { imagesTestIds } from './testIds';
import { useBookImages } from './useBookImages';

interface ImagePickerProps {
  bookId: string;
  /** Where the image will sit; each image's effective dpi is worked out for it. */
  placement: Placement;
  value: string | null;
  onChange: (assetId: string) => void;
}

const statusLabels: Record<string, string> = {
  processing: 'Preparing…',
  failed: 'Failed',
  none: 'No preview',
};

const size = (asset: Asset) =>
  asset.widthPx !== null && asset.heightPx !== null ? `${asset.widthPx}×${asset.heightPx}` : null;

const dpiText = (dpi: AssetDpi) => `${Math.round(dpi.effectiveDpi)} dpi`;

function DpiNote({ asset, placement }: { asset: Asset; placement: Placement }) {
  const { dpi } = asset;
  if (dpi === null) return null;
  const where = `${placementLabels[placement].toLowerCase()} (${dpi.placedWidthIn.toFixed(2)} in wide)`;
  return (
    <span
      data-testid={imagesTestIds.selectedDpi}
      data-status={dpi.status}
      className={dpi.status === 'low' ? 'font-semibold text-warn' : 'text-muted'}
    >
      {dpi.status === 'low'
        ? `Low resolution: ${dpiText(dpi)} at ${where}. Print needs ${dpi.targetDpi} dpi, about ${Math.ceil(dpi.placedWidthIn * dpi.targetDpi)} px wide.`
        : `${dpiText(dpi)} at ${where}.`}
    </span>
  );
}

// The book's and its series' images as a grid of web-rendition thumbnails, each with its size, rendition status and
// effective dpi at `placement`, plus Upload image (PNG, JPEG or WebP), which adds the new image and selects it.
export function ImagePicker({ bookId, placement, value, onChange }: ImagePickerProps) {
  const queryClient = useQueryClient();
  const images = useBookImages(bookId, placement);
  const input = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const selected = images.data?.find((a) => a.id === value);

  const upload = async (file: File) => {
    setUploading(true);
    setUploadError(null);
    try {
      const asset = await assetApi.upload(bookId, file);
      queryClient.setQueriesData<Asset[]>({ queryKey: bookImagesKey(bookId) }, (list) =>
        list === undefined || list.some((a) => a.id === asset.id) ? list : [asset, ...list],
      );
      onChange(asset.id);
      await queryClient.invalidateQueries({ queryKey: bookImagesKey(bookId) });
    } catch (e) {
      setUploadError(e instanceof Error ? e.message : 'The image could not be uploaded.');
    } finally {
      setUploading(false);
    }
  };

  return (
    <div data-testid={imagesTestIds.picker} className="flex min-w-0 flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="flex-1 text-xs text-muted">
          {images.data ? `${images.data.length} in this book` : 'Loading images…'}
        </span>
        <input
          ref={input}
          data-testid={imagesTestIds.uploadInput}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.target.value = '';
            if (file !== undefined) void upload(file);
          }}
        />
        <button
          type="button"
          data-testid={imagesTestIds.upload}
          disabled={uploading}
          onClick={() => input.current?.click()}
          className="inline-flex h-[var(--control-height)] cursor-pointer items-center rounded-sm border border-border bg-surface px-2 text-sm font-medium text-text hover:bg-surface-2 disabled:cursor-wait disabled:opacity-60"
        >
          {uploading ? 'Uploading…' : 'Upload image'}
        </button>
      </div>
      {uploadError !== null && (
        <p role="alert" data-testid={imagesTestIds.uploadError} className="m-0 text-xs text-danger">
          {uploadError}
        </p>
      )}
      {images.isError && (
        <p role="alert" data-testid={imagesTestIds.loadError} className="m-0 text-xs text-danger">
          {images.error.message}
        </p>
      )}
      {images.data?.length === 0 && (
        <p
          data-testid={imagesTestIds.empty}
          className="m-0 rounded-md border border-dashed border-border p-3 text-center text-sm text-muted"
        >
          No images in this book yet. Upload a PNG, JPEG or WebP.
        </p>
      )}
      {images.data !== undefined && images.data.length > 0 && (
        <div
          role="radiogroup"
          aria-label="Images"
          className="grid max-h-64 grid-cols-[repeat(auto-fill,minmax(104px,1fr))] gap-1.5 overflow-auto p-0.5"
        >
          {images.data.map((asset) => {
            const thumbnail = webRendition(asset);
            const checked = asset.id === value;
            return (
              <button
                key={asset.id}
                type="button"
                role="radio"
                aria-checked={checked}
                data-testid={imagesTestIds.option(asset.id)}
                title={asset.filename}
                onClick={() => onChange(asset.id)}
                className={`flex min-w-0 cursor-pointer flex-col gap-1 rounded-md border p-1 text-left focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-accent ${
                  checked
                    ? 'border-accent bg-accent-soft'
                    : 'border-border bg-surface hover:bg-surface-2'
                }`}
              >
                <span className="relative grid aspect-[4/3] place-items-center overflow-hidden rounded-sm bg-surface-2">
                  {thumbnail !== null ? (
                    <img src={thumbnail} alt="" className="size-full object-contain" />
                  ) : (
                    <span
                      data-testid={imagesTestIds.optionStatus(asset.id)}
                      data-status={asset.status}
                      className={`px-1 text-center text-2xs ${asset.status === 'failed' ? 'text-danger' : 'text-muted'}`}
                    >
                      {statusLabels[asset.status] ?? asset.status}
                    </span>
                  )}
                </span>
                <span className="truncate text-xs text-text">{asset.filename}</span>
                <span className="flex gap-1 text-2xs text-muted tabular-nums">
                  {size(asset) && <span>{size(asset)}</span>}
                  {asset.dpi && (
                    <span
                      data-testid={imagesTestIds.optionDpi(asset.id)}
                      data-status={asset.dpi.status}
                      className={asset.dpi.status === 'low' ? 'font-semibold text-warn' : ''}
                    >
                      · {dpiText(asset.dpi)}
                    </span>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      )}
      {selected && (
        <p
          data-testid={imagesTestIds.selected}
          className="m-0 flex flex-col gap-0.5 text-xs [overflow-wrap:anywhere]"
        >
          <span className="text-text">
            {selected.filename}
            {size(selected) && <span className="text-muted"> · {size(selected)} px</span>}
            {selected.scope === 'series' && <span className="text-muted"> · from the series</span>}
          </span>
          <DpiNote asset={selected} placement={placement} />
          {selected.status === 'failed' && selected.error && (
            <span className="text-danger">{selected.error}</span>
          )}
        </p>
      )}
    </div>
  );
}
