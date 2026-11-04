import { api, type RequestOptions } from './client';

// C#: Codex.Contracts.KnowledgeBase.
export interface KbFieldDef {
  key: string;
  label: string;
}

export interface KbCategory {
  id: string;
  name: string;
  tag: string | null;
  colour: string | null;
  position: string;
  fieldDefs: KbFieldDef[];
  fromSeries: boolean;
  seriesName: string | null;
}

export interface KbEntrySummary {
  id: string;
  categoryId: string;
  name: string;
  aliases: string[];
  sceneCount: number;
  useCount: number;
  fromSeries: boolean;
  seriesName: string | null;
  alwaysInContext: boolean;
  /** The first non-empty line of the summary, at most 140 characters (`…` when cut); empty when there is none. */
  summaryLine: string;
}

export interface Kb {
  categories: KbCategory[];
  entries: KbEntrySummary[];
}

export interface LinkKbEntryRequest {
  entryId: string;
}

export interface SetBeatContextRequest {
  entryIds: string[];
}

export const kbKey = (bookId: string) => ['kb', bookId] as const;

export const getKb = (bookId: string, options?: RequestOptions) =>
  api.get<Kb>(`/api/books/${bookId}/kb`, options);

export const linkSceneEntry = (sceneId: string, entryId: string) =>
  api.post<void>(`/api/scenes/${sceneId}/kb`, { entryId } satisfies LinkKbEntryRequest);

export const unlinkSceneEntry = (sceneId: string, entryId: string) =>
  api.del<void>(`/api/scenes/${sceneId}/kb/${entryId}`);

export const setBeatContext = (beatId: string, entryIds: string[]) =>
  api.put<void>(`/api/beats/${beatId}/context`, { entryIds } satisfies SetBeatContextRequest);
