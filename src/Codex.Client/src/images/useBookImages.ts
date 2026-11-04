import { useQuery } from '@tanstack/react-query';
import { assetApi, bookImagesKey, type Asset, type Placement } from '@codex/core/api/assets';
import { layoutApi, layoutKey } from '@codex/core/api/layout';

const POLL_MS = 2000;

/**
 * The images a book can place (its own and its series'), each with its effective dpi at `placement` in the book's
 * current trim and template columns. While any image's renditions are still being made, the list is read again every
 * two seconds so thumbnails and statuses arrive on their own.
 */
export function useBookImages(bookId: string, placement: Placement) {
  const layout = useQuery({
    queryKey: layoutKey(bookId),
    queryFn: ({ signal }) => layoutApi.state(bookId, { signal }),
  });
  const template = layout.data?.templates.find((t) => t.slug === layout.data?.template);
  const dpi =
    layout.data && template
      ? { trim: layout.data.trim, columns: template.columns, placement }
      : null;
  return useQuery({
    queryKey: [...bookImagesKey(bookId), dpi],
    queryFn: ({ signal }) => assetApi.list(bookId, dpi, { signal }),
    // Without a layout (it failed to load) the images still list, just without dpi.
    enabled: layout.data !== undefined || layout.isError,
    refetchInterval: (query) =>
      (query.state.data as Asset[] | undefined)?.some((a) => a.status === 'processing')
        ? POLL_MS
        : false,
  });
}
