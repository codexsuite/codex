import { useState } from 'react';
import type { Placement } from '@codex/core/api/assets';
import { ImagePicker } from '../images/ImagePicker';
import { pickerHarnessValue } from './imagePickerHarnessIds';

// The picker as a dialog holds it: the chosen image in state, shown beside it so a spec can read the selection.
export function ImagePickerHarness({
  bookId,
  placement = 'column',
  initial = null,
}: {
  bookId: string;
  placement?: Placement;
  initial?: string | null;
}) {
  const [value, setValue] = useState<string | null>(initial);
  return (
    <div>
      <ImagePicker bookId={bookId} placement={placement} value={value} onChange={setValue} />
      <output data-testid={pickerHarnessValue}>{value ?? ''}</output>
    </div>
  );
}
