import type { MentionEntry } from './entries';
import { proseTestIds } from './testIds';
import type { PopupView } from './suggestion/usePopup';

export function MentionPicker({ view }: { view: PopupView<MentionEntry> }) {
  return (
    <ul
      role="listbox"
      className="prose-popup"
      data-testid={proseTestIds.mentionPicker}
      style={popupPosition(view.rect)}
      onMouseDown={(e) => e.preventDefault()}
    >
      {view.items.length === 0 && <li className="prose-popup-empty">No matching entry</li>}
      {view.items.map((entry, index) => (
        <li
          key={entry.id}
          role="option"
          aria-selected={index === view.index}
          className="prose-popup-item"
          data-testid={proseTestIds.mentionOption(entry.id)}
          onClick={() => view.select(entry)}
        >
          <span
            className="prose-popup-dot"
            style={{ background: `var(--category-${entry.category}, var(--color-muted, #52627c))` }}
          />
          <span>{entry.name}</span>
          <span className="prose-popup-meta">{entry.category}</span>
        </li>
      ))}
    </ul>
  );
}

// .prose-popup's max-height in prose.css.
const popupMaxHeight = 280;
const caretGap = 4;
const windowMargin = 8;

// The popup is fixed, so scrolling cannot bring back a part outside the window: without room below the caret, it opens above.
export function popupPosition(rect: DOMRect | null): React.CSSProperties {
  if (!rect) return { position: 'fixed', left: 0, top: 0 };
  const below = window.innerHeight - rect.bottom - caretGap - windowMargin;
  const above = rect.top - caretGap - windowMargin;
  if (below >= popupMaxHeight || below >= above) {
    return {
      position: 'fixed',
      left: rect.left,
      top: Math.max(windowMargin, rect.bottom + caretGap),
      maxHeight: Math.min(popupMaxHeight, below),
    };
  }
  return {
    position: 'fixed',
    left: rect.left,
    bottom: Math.max(windowMargin, window.innerHeight - rect.top + caretGap),
    maxHeight: Math.min(popupMaxHeight, above),
  };
}
