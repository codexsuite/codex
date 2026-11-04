import bookHeader from '../../../Codex.TestSupport/Fixtures/book-header.json';
import type { BookHeader } from '@codex/core/api/types';
import { BookBar } from '../shell/BookBar';
import { PaletteProvider } from '../shell/CommandPalette';
import { SlotFill, SlotsProvider } from '../shell/Slots';

// What a lane page does: fills slots from anywhere in the tree. Mounted inside the shell providers only.
export function SlotHarness() {
  return (
    <SlotsProvider>
      <PaletteProvider>
        <BookBar book={bookHeader satisfies BookHeader} />
        <SlotFill slot="badge">
          <b>3</b>
        </SlotFill>
        <SlotFill slot="scope">
          <span>Scope: Full manuscript</span>
        </SlotFill>
        <SlotFill slot="format">
          <button type="button">Format</button>
        </SlotFill>
        <SlotFill slot="focus">
          <button type="button">Focus</button>
        </SlotFill>
        <SlotFill slot="saveStatus">
          <span>Saved</span>
        </SlotFill>
        <SlotFill slot="palette">
          <input aria-label="Palette search" />
        </SlotFill>
      </PaletteProvider>
    </SlotsProvider>
  );
}
