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

export function popupPosition(rect: DOMRect | null): React.CSSProperties {
  return rect
    ? { position: 'fixed', left: rect.left, top: rect.bottom + 4 }
    : { position: 'fixed', left: 0, top: 0 };
}
