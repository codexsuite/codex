import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { layoutLink } from '@codex/core/shell/links';
import { SlotFill } from '../../shell/Slots';
import type { WriteFormat } from './formatStore';
import { writeTestIds } from './testIds';

interface FormatMenuProps {
  format: WriteFormat;
  onChange: (change: Partial<WriteFormat>) => void;
  /** The scene or chapter in scope, as the Layout tab's link names it; empty for a wider scope. */
  previewAnchor?: { scene?: string; chapter?: string };
  /** The Preview split pane beside the prose: open or not, and how to change it. */
  preview?: { open: boolean; onChange: (open: boolean) => void };
}

interface Row {
  label: string;
  key: string;
  options: { value: string; text: string; pressed: boolean; change: Partial<WriteFormat> }[];
}

function rows(format: WriteFormat): Row[] {
  return [
    {
      label: 'Text width',
      key: 'width',
      options: (['narrow', 'medium', 'wide'] as const).map((width) => ({
        value: width,
        text: width[0].toUpperCase() + width.slice(1),
        pressed: format.width === width,
        change: { width },
      })),
    },
    {
      label: 'Paragraphs',
      key: 'paragraphs',
      options: [
        {
          value: 'spaced',
          text: 'Spaced',
          pressed: format.paragraphs === 'spaced',
          change: { paragraphs: 'spaced' },
        },
        {
          value: 'indent',
          text: 'Indented',
          pressed: format.paragraphs === 'indent',
          change: { paragraphs: 'indent' },
        },
      ],
    },
    {
      label: 'Scene beats',
      key: 'beats',
      options: [
        { value: 'show', text: 'Show', pressed: !format.hideBeats, change: { hideBeats: false } },
        { value: 'hide', text: 'Hide', pressed: format.hideBeats, change: { hideBeats: true } },
      ],
    },
    {
      label: 'Summaries',
      key: 'summaries',
      options: [
        {
          value: 'show',
          text: 'Show',
          pressed: !format.hideSummaries,
          change: { hideSummaries: false },
        },
        {
          value: 'hide',
          text: 'Hide',
          pressed: format.hideSummaries,
          change: { hideSummaries: true },
        },
      ],
    },
    {
      label: 'Codex names',
      key: 'names',
      options: [
        {
          value: 'underline',
          text: 'Underline',
          pressed: format.underlineNames,
          change: { underlineNames: true },
        },
        {
          value: 'plain',
          text: 'Plain',
          pressed: !format.underlineNames,
          change: { underlineNames: false },
        },
      ],
    },
  ];
}

// The book bar's Format button and its popover: text width, paragraph style, beats, summaries, Codex names.
export function FormatMenu({ format, onChange, previewAnchor = {}, preview }: FormatMenuProps) {
  const { bookId = '' } = useParams();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      // Esc closes the popover only; it must not also leave Focus.
      event.stopPropagation();
      setOpen(false);
      button.current?.focus();
    };
    const onPointer = (event: PointerEvent) => {
      if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('pointerdown', onPointer);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('pointerdown', onPointer);
    };
  }, [open]);

  return (
    <SlotFill slot="format">
      <div ref={root} className="relative">
        <button
          ref={button}
          type="button"
          data-testid={writeTestIds.formatButton}
          aria-haspopup="dialog"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
          className="h-[var(--control-height)] cursor-pointer rounded-sm border-0 px-2.5 text-sm font-medium text-text hover:bg-surface-2 bg-transparent aria-expanded:bg-accent-soft"
        >
          Format
        </button>
        {open && (
          <div
            role="dialog"
            aria-label="Format"
            data-testid={writeTestIds.formatPopover}
            className="absolute top-full right-0 z-50 mt-1 grid grid-cols-[auto_1fr] items-center gap-x-3 gap-y-2 rounded-md border border-border bg-surface p-3 text-sm shadow-lg"
          >
            {rows(format).map((row) => (
              <FormatRow key={row.key} row={row} onChange={onChange} />
            ))}
            {preview && <PreviewRow {...preview} />}
            <Link
              data-testid={writeTestIds.formatPreview}
              to={layoutLink(bookId, previewAnchor)}
              className="col-span-2 text-accent no-underline hover:underline"
            >
              Preview in Layout
            </Link>
          </div>
        )}
      </div>
    </SlotFill>
  );
}

function FormatRow({
  row,
  onChange,
}: {
  row: Row;
  onChange: (change: Partial<WriteFormat>) => void;
}) {
  return (
    <>
      <span className="text-muted">{row.label}</span>
      <div className="inline-flex h-[26px] justify-self-start overflow-hidden rounded-sm border border-border">
        {row.options.map((option) => (
          <button
            key={option.value}
            type="button"
            data-testid={writeTestIds.formatOption(row.key, option.value)}
            aria-pressed={option.pressed}
            onClick={() => onChange(option.change)}
            className={`cursor-pointer border-0 border-r border-border px-2 text-sm last:border-r-0 ${
              option.pressed ? 'bg-accent-soft font-semibold text-text' : 'bg-surface text-muted'
            }`}
          >
            {option.text}
          </button>
        ))}
      </div>
    </>
  );
}

// Show or hide the Preview pane (also Ctrl+Alt+P); the same look as the rows above.
function PreviewRow({ open, onChange }: { open: boolean; onChange: (open: boolean) => void }) {
  return (
    <>
      <span className="text-muted" title="Ctrl+Alt+P">
        Preview
      </span>
      <div className="inline-flex h-[26px] justify-self-start overflow-hidden rounded-sm border border-border">
        {[true, false].map((value) => (
          <button
            key={String(value)}
            type="button"
            data-testid={writeTestIds.formatOption('pane', value ? 'show' : 'hide')}
            aria-pressed={open === value}
            onClick={() => onChange(value)}
            className={`cursor-pointer border-0 border-r border-border px-2 text-sm last:border-r-0 ${
              open === value ? 'bg-accent-soft font-semibold text-text' : 'bg-surface text-muted'
            }`}
          >
            {value ? 'Show' : 'Hide'}
          </button>
        ))}
      </div>
    </>
  );
}
