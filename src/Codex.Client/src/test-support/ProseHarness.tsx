import { useState } from 'react';
import {
  MarkdownSurface,
  ProseEditor,
  richBlockSlashItems,
  SummaryEditor,
  type MentionEntry,
  type SlashItem,
  type SurfaceKey,
} from '../prose';

// Component-test host: a controlled editor that prints the Markdown it last emitted and counts slash-item runs.

const ENTRIES: MentionEntry[] = [
  {
    id: '0199c0de-0000-7000-8000-000b0000000b',
    name: 'Kent Beck',
    category: 'person',
    aliases: ['Beck'],
  },
  {
    id: '0199c0de-0000-7000-8000-000b00000005',
    name: "Goodhart's Law",
    category: 'law',
    aliases: [],
  },
];

export function ProseHarness({
  initial = '',
  summary = false,
  surface,
}: {
  initial?: string;
  summary?: boolean;
  /** Mounts the Rich / Source toggle around the editor for this surface instead of the bare editor. */
  surface?: SurfaceKey;
}) {
  const [markdown, setMarkdown] = useState(initial);
  const [changes, setChanges] = useState<boolean[]>([]);
  const [runs, setRuns] = useState<string[]>([]);
  const [readOnly, setReadOnly] = useState(false);
  const slashItems: SlashItem[] = [
    ...richBlockSlashItems(),
    {
      id: 'disabled',
      label: 'Disabled command',
      group: 'Test',
      enabled: false,
      run: () => setRuns((r) => [...r, 'disabled']),
    },
    {
      id: 'enabled',
      label: 'Enabled command',
      group: 'Test',
      enabled: true,
      run: () => setRuns((r) => [...r, 'enabled']),
    },
  ];
  return (
    <div>
      {surface ? (
        <MarkdownSurface
          surface={surface}
          variant={summary ? 'summary' : 'prose'}
          value={markdown}
          onChange={(next, fromSource) => {
            setMarkdown(next);
            setChanges((c) => [...c, fromSource]);
          }}
          entries={ENTRIES}
        />
      ) : summary ? (
        <SummaryEditor value={markdown} onChange={setMarkdown} entries={ENTRIES} />
      ) : (
        <ProseEditor
          value={markdown}
          onChange={setMarkdown}
          entries={ENTRIES}
          slashItems={slashItems}
          readOnly={readOnly}
        />
      )}
      <button
        type="button"
        data-testid="harness-toggle-read-only"
        onClick={() => setReadOnly((r) => !r)}
      >
        {readOnly ? 'Editable' : 'Read only'}
      </button>
      <pre data-testid="harness-markdown">{markdown}</pre>
      <span data-testid="harness-changes">
        {changes.map((c) => (c ? 'source' : 'rich')).join(',')}
      </span>
      <span data-testid="harness-runs">{runs.join(',')}</span>
    </div>
  );
}
