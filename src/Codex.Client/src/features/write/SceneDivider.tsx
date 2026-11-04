import { writeTestIds } from './testIds';

interface SceneDividerProps {
  /** The scene above the divider. */
  sceneId: string;
}

// The ◇ ornament between two scenes of a chapter.
export function SceneDivider({ sceneId }: SceneDividerProps) {
  return (
    <div className="write-row" aria-hidden="true">
      <div
        data-testid={writeTestIds.divider(sceneId)}
        className="grid h-14 place-items-center text-muted"
      >
        <svg
          viewBox="0 0 120 12"
          fill="none"
          stroke="currentColor"
          strokeWidth="1"
          className="h-3 w-[120px]"
        >
          <path d="M2 6h44M74 6h44" />
          <circle cx="50" cy="6" r="1.6" fill="currentColor" />
          <circle cx="70" cy="6" r="1.6" fill="currentColor" />
          <path d="M60 1.5 64.5 6 60 10.5 55.5 6z" />
        </svg>
      </div>
    </div>
  );
}
