import type { Editor } from '@tiptap/react';
import { popupPosition } from './MentionPicker';
import { isSlashItemEnabled, type SlashItem } from './slashMenu';
import { proseTestIds } from './testIds';
import type { PopupView } from './suggestion/usePopup';

export function SlashMenu({ view, editor }: { view: PopupView<SlashItem>; editor: Editor }) {
  return (
    <div
      role="listbox"
      className="prose-popup"
      data-testid={proseTestIds.slashMenu}
      style={popupPosition(view.rect)}
      onMouseDown={(e) => e.preventDefault()}
    >
      {view.items.length === 0 && <div className="prose-popup-empty">No matching command</div>}
      {view.items.map((item, index) => {
        const enabled = isSlashItemEnabled(item, editor);
        const startsGroup = index === 0 || view.items[index - 1].group !== item.group;
        return (
          <div key={item.id}>
            {startsGroup && <div className="prose-popup-group">{item.group}</div>}
            <div
              role="option"
              aria-selected={index === view.index}
              aria-disabled={!enabled}
              className="prose-popup-item"
              data-testid={proseTestIds.slashItem(item.id)}
              onClick={() => view.select(item)}
            >
              {item.label}
            </div>
          </div>
        );
      })}
    </div>
  );
}
