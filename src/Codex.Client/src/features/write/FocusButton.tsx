import { SlotFill } from '../../shell/Slots';
import { writeTestIds } from './testIds';

interface FocusButtonProps {
  focused: boolean;
  onToggle: () => void;
}

// The book bar's Focus button. Esc also leaves Focus (WritePage listens for it).
export function FocusButton({ focused, onToggle }: FocusButtonProps) {
  return (
    <SlotFill slot="focus">
      <button
        type="button"
        data-testid={writeTestIds.focusButton}
        aria-pressed={focused}
        title="Focus (Esc to leave)"
        onClick={onToggle}
        className={`h-[var(--control-height)] cursor-pointer rounded-sm border-0 px-2.5 text-sm font-medium text-text hover:bg-surface-2 ${focused ? 'bg-accent-soft' : 'bg-transparent'}`}
      >
        Focus
      </button>
    </SlotFill>
  );
}
