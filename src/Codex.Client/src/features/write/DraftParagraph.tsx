import { useParams } from 'react-router-dom';
import type { Marker } from '@codex/core/api/references';
import { markerTone } from './markerTone';
import { splitRefs } from '@codex/core/features/write/draftText';
import { useOpenMarkerCard } from './markerCardContext';
import { useMarkers } from './useMarkers';
import { writeTestIds } from './testIds';

// One paragraph of a pending draft. Its `[words](ref:<use>)` links are the draft's source markers: the words take the
// status tone and the source's number follows them, as in the scene text.
export function DraftParagraph({ text }: { text: string }) {
  const { bookId = '' } = useParams();
  const { byUse } = useMarkers(bookId);
  const open = useOpenMarkerCard();
  return (
    <p>
      {splitRefs(text).map((part, i) => {
        if (part.useId === null) return part.text;
        const marker: Marker | undefined = byUse.get(part.useId);
        if (!marker) return part.text;
        return (
          <span key={i}>
            {part.text !== '' && (
              <span className="write-ref" data-tone={markerTone(marker.status)}>
                {part.text}
              </span>
            )}
            <sup
              data-testid={writeTestIds.marker(marker.useId)}
              data-marker-use={marker.useId}
              data-source={marker.sourceId}
              data-number={marker.number}
              data-status={marker.status}
              data-tone={markerTone(marker.status)}
              className="write-marker"
              role="button"
              tabIndex={0}
              onClick={(event) => open(marker, event.currentTarget)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') open(marker, event.currentTarget);
              }}
            >
              {marker.number}
            </sup>
          </span>
        );
      })}
    </p>
  );
}
