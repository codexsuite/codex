// The beat being dragged by its grip. A module-level record, because the drag starts in one scene's card and ends on
// another scene's row, and the browser only lets a drop read the data types, not the values, during dragover.
export interface BeatDrag {
  beatId: string;
  sceneId: string;
}

let current: BeatDrag | null = null;

export const beatDrag = {
  start(drag: BeatDrag) {
    current = drag;
  },
  end() {
    current = null;
  },
  get(): BeatDrag | null {
    return current;
  },
};
