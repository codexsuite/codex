// What a Knowledge base entry carries when it is dragged (sidebar -> Write / Plan drop targets).
export const entryDragType = 'application/x-codex-entry';

export interface EntryDragPayload {
  entryId: string;
  name: string;
}

export function setEntryDragData(dataTransfer: DataTransfer, payload: EntryDragPayload) {
  dataTransfer.setData(entryDragType, JSON.stringify(payload));
  dataTransfer.effectAllowed = 'copyLink';
}

export function hasEntryDragData(dataTransfer: DataTransfer): boolean {
  return dataTransfer.types.includes(entryDragType);
}

/** The payload of a drop, or null when the drag was not an entry. */
export function readEntryDragData(dataTransfer: DataTransfer): EntryDragPayload | null {
  const raw = dataTransfer.getData(entryDragType);
  if (raw === '') return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed === 'object' && parsed !== null && 'entryId' in parsed && 'name' in parsed) {
      const { entryId, name } = parsed;
      if (typeof entryId === 'string' && typeof name === 'string') return { entryId, name };
    }
  } catch {
    // not ours
  }
  return null;
}
