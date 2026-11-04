import { useState } from 'react';
import type { KbEntry } from '@codex/core/api/kbEntries';
import { PlusIcon } from './icons';
import { categoryTint, miniButton, railHeading } from './kbStyles';
import { RelatedPicker } from './RelatedPicker';
import { kbTestIds } from './testIds';
import { useKbCategories } from './useKbCategories';
import { useKbEntries } from './useKbEntries';
import { useRelated } from './useRelated';
import { useCanWrite } from '../../shell/bookAccess';

interface RelatedSectionProps {
  bookId: string;
  entry: KbEntry;
}

// The rail's Related part: a category-tinted chip per linked entry with × (on hover) to unlink, and a picker to
// link another. The chip's colour comes from the book's categories, matched by the category name the entry carries.
export function RelatedSection({ bookId, entry }: RelatedSectionProps) {
  const choices = useKbEntries(bookId);
  const categories = useKbCategories(bookId);
  const related = useRelated(entry);
  const [picking, setPicking] = useState(false);
  // Below write (#80) related entries list, with no link or unlink.
  const canWrite = useCanWrite();
  const [error, setError] = useState<string | null>(null);

  const run = async (action: () => Promise<unknown>) => {
    try {
      await action();
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That change could not be saved.');
    }
  };
  const linked = new Set(entry.related.map((r) => r.id));
  const free = choices.filter((c) => c.id !== entry.id && !linked.has(c.id));
  const colourOf = (categoryName: string) =>
    categories.find((c) => c.name === categoryName)?.colour ?? null;

  return (
    <section data-testid={kbTestIds.related} aria-label="Related">
      <div className="relative">
        <h4 className={`${railHeading} mb-1.5`}>
          Related
          {canWrite && (
            <button
              type="button"
              data-testid={kbTestIds.relatedAdd}
              aria-label="Link an entry"
              aria-expanded={picking}
              onClick={() => setPicking(!picking)}
              className={`${miniButton} ml-auto tracking-normal normal-case`}
            >
              <PlusIcon size={11} />
              Link entry
            </button>
          )}
        </h4>
        {picking && (
          <RelatedPicker
            choices={free}
            onPick={(other) => {
              setPicking(false);
              void run(() => related.add(other.id, other.name));
            }}
          />
        )}
      </div>
      {entry.related.length === 0 && (
        <span className="text-xs text-muted italic">No related entries.</span>
      )}
      <div className="flex flex-wrap gap-1">
        {entry.related.map((r) => (
          <span
            key={r.id}
            data-testid={kbTestIds.relatedChip(r.id)}
            title={r.categoryName}
            style={{
              ['--cat' as string]: colourOf(r.categoryName)
                ? `var(--${colourOf(r.categoryName)})`
                : 'var(--color-muted)',
            }}
            className={`group inline-flex h-[19px] items-center gap-1 rounded-[4px] px-1.5 text-2xs leading-[1.55] font-medium whitespace-nowrap text-text ${categoryTint}`}
          >
            <i className="h-1.5 w-1.5 rounded-[2px] bg-[var(--cat)]" />
            <b className="font-medium">{r.name}</b>
            {canWrite && (
              <button
                type="button"
                data-testid={kbTestIds.relatedRemove(r.id)}
                aria-label={`Unlink ${r.name}`}
                onClick={() => void run(() => related.remove(r.id, r.name))}
                className="-mr-[3px] border-0 bg-transparent p-0 text-xs leading-none text-muted [@media(hover:hover)]:w-0 [@media(hover:hover)]:overflow-hidden [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-focus-within:w-auto [@media(hover:hover)]:group-focus-within:opacity-100 [@media(hover:hover)]:group-hover:w-auto [@media(hover:hover)]:group-hover:opacity-100"
              >
                ×
              </button>
            )}
          </span>
        ))}
      </div>
      {error && (
        <p data-testid={kbTestIds.relatedError} role="alert" className="m-0 text-xs text-danger">
          {error}
        </p>
      )}
    </section>
  );
}
