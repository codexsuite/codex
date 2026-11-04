import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { bookKey } from '@codex/core/api/books';
import { useBook } from '../../api/booksQueries';
import { settingsApi } from '@codex/core/api/settings';
import { showToast } from '@codex/core/editing/toastStore';
import { Cover } from '../library/Cover';
import { FieldRow } from './FieldRow';
import { settingsTestIds } from './testIds';

const button =
  'inline-flex h-6 cursor-pointer items-center rounded-sm border px-2 text-xs font-medium whitespace-nowrap text-text disabled:cursor-not-allowed disabled:opacity-45';

// The front cover: Replace… uploads an image and makes it the cover; Remove goes back to the generated cover.
// Both refresh the book header (book bar, this row) and Library.
export function CoverRow({ bookId, title }: { bookId: string; title: string }) {
  const queryClient = useQueryClient();
  const book = useBook(bookId).data;
  const picker = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);

  const run = async (work: () => Promise<void>, fallback: string) => {
    setBusy(true);
    try {
      await work();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: bookKey(bookId) }),
        queryClient.invalidateQueries({ queryKey: ['library'] }),
      ]);
    } catch (e) {
      showToast({ message: e instanceof Error ? e.message : fallback });
    } finally {
      setBusy(false);
    }
  };

  const replace = (file: File) =>
    run(async () => {
      const image = await settingsApi.uploadImage(bookId, file);
      await settingsApi.setCover(bookId, image.id);
    }, 'The cover could not be changed.');

  const remove = () =>
    run(async () => {
      await settingsApi.removeCover(bookId);
      showToast({
        message: 'Cover removed. Library shows a generated cover in the book’s colours.',
      });
    }, 'The cover could not be removed.');

  const hasUpload = book?.coverUrl != null;
  return (
    <FieldRow
      label="Cover"
      help="Front cover for Library and the bookbar. The printed cover is set in Layout."
    >
      <div className="flex items-end gap-3">
        {book !== undefined && (
          <Cover
            palette={book.coverPalette}
            imageUrl={book.coverUrl}
            title={title}
            testId={settingsTestIds.cover}
            className="[--cover-width:64px]"
          />
        )}
        <input
          ref={picker}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0];
            e.target.value = '';
            if (file !== undefined) void replace(file);
          }}
        />
        <div className="flex flex-wrap gap-1.5">
          <button
            type="button"
            data-testid={settingsTestIds.coverReplace}
            disabled={busy}
            title="Upload a PNG, JPEG or WebP image"
            onClick={() => picker.current?.click()}
            className={`${button} border-border bg-surface`}
          >
            Replace…
          </button>
          <button
            type="button"
            data-testid={settingsTestIds.coverRemove}
            disabled={busy || !hasUpload}
            title={hasUpload ? 'Use a generated cover in the book’s colours' : 'No uploaded cover'}
            onClick={() => void remove()}
            className={`${button} border-transparent bg-transparent`}
          >
            Remove
          </button>
        </div>
      </div>
    </FieldRow>
  );
}
