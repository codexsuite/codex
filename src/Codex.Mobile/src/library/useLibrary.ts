import { defaultLibraryParams, getLibrary, libraryKey } from '@codex/core/api/library';
import { useQuery } from '@tanstack/react-query';

/** `GET /api/library` with the default params: the Library, the Continue card and the book switcher share this entry. */
export function useLibrary() {
  return useQuery({
    queryKey: libraryKey(defaultLibraryParams),
    queryFn: ({ signal }) => getLibrary(defaultLibraryParams, signal),
  });
}
