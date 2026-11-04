import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  groupOptions,
  sortOptions,
  statusOptions,
  type LibraryGroup,
  type LibraryParams,
  type LibrarySort,
  type LibraryStatus,
} from '@codex/core/api/library';

export type LibraryViewMode = 'grid' | 'list';

export interface LibraryView extends LibraryParams {
  view: LibraryViewMode;
}

function oneOf<T extends string>(options: readonly T[], value: string | null): T | undefined {
  return options.find((option) => option === value);
}

/** Reads the screen state from the URL search params; anything unrecognised falls back to the default. */
export function readView(params: URLSearchParams): LibraryView {
  return {
    q: params.get('q') ?? '',
    sort: oneOf(sortOptions, params.get('sort')) ?? 'lastEdited',
    group: oneOf(groupOptions, params.get('group')) ?? 'series',
    status: oneOf(statusOptions, params.get('status')) ?? null,
    view: params.get('view') === 'list' ? 'list' : 'grid',
  };
}

/** The search params for a view: defaults are omitted so the URL stays short. */
export function writeView(view: LibraryView): URLSearchParams {
  const params = new URLSearchParams();
  if (view.q !== '') params.set('q', view.q);
  if (view.sort !== 'lastEdited') params.set('sort', view.sort);
  if (view.group !== 'series') params.set('group', view.group);
  if (view.status !== null) params.set('status', view.status);
  if (view.view === 'list') params.set('view', 'list');
  return params;
}

export interface LibraryViewState {
  view: LibraryView;
  update: (change: Partial<LibraryView>, options?: { replace?: boolean }) => void;
}

export function useLibraryView(): LibraryViewState {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { hash } = useLocation();
  const view = readView(params);
  return {
    view,
    // The hash stays: on the Series screen (`#series=<id>`) the toolbar changes the view, not the screen.
    update: (change, options) =>
      void navigate({ search: writeView({ ...view, ...change }).toString(), hash }, options),
  };
}

export type { LibraryGroup, LibrarySort, LibraryStatus };
