import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { getSourcesCsl, sourcesCslKey } from '@codex/core/api/referenceSources';
import { usePublicationSettings } from '../../api/settingsQueries';
import { ExportMenu } from './ExportMenu';
import {
  citationStyles,
  cslFileKey,
  formatBibliography,
  loadCslFile,
  type CitationStyle,
} from './bibliography';
import { refsTestIds } from './testIds';

// A page preview of the bibliography in the chosen style: cited sources only, sorted by the style, with sources
// still at risk underlined in danger and a granted permission's credit line after its entry. The style starts at the
// book's saved citation style (Settings) until the writer picks another here.
export function BibliographyView({ bookId }: { bookId: string }) {
  const [picked, setPicked] = useState<CitationStyle | null>(null);
  const saved = usePublicationSettings(bookId).data?.style.value;
  const style = picked ?? saved ?? 'chicago';
  const file = citationStyles.find((s) => s.value === style)!.file;
  const sources = useQuery({
    queryKey: sourcesCslKey(bookId),
    queryFn: ({ signal }) => getSourcesCsl(bookId, { signal }),
  });
  const styleXml = useQuery({
    queryKey: cslFileKey(file),
    queryFn: () => loadCslFile(file),
    staleTime: Infinity,
  });
  const locale = useQuery({
    queryKey: cslFileKey('locales-en-US.xml'),
    queryFn: () => loadCslFile('locales-en-US.xml'),
    staleTime: Infinity,
  });

  const failure = sources.error ?? styleXml.error ?? locale.error;
  const ready =
    sources.data !== undefined && styleXml.data !== undefined && locale.data !== undefined;
  const formatted = useMemo(() => {
    if (!ready) return null;
    try {
      return { entries: formatBibliography(sources.data, styleXml.data, locale.data), error: null };
    } catch (error) {
      return {
        entries: null,
        error: error instanceof Error ? error.message : 'The bibliography could not be formatted.',
      };
    }
  }, [ready, sources.data, styleXml.data, locale.data]);

  const message = failure?.message ?? formatted?.error ?? null;
  return (
    <section
      data-testid={refsTestIds.bibliography}
      className="mx-auto flex max-w-3xl flex-col gap-3 p-4"
    >
      <div className="flex flex-wrap items-center gap-2">
        <label className="flex items-center gap-2 text-sm text-muted">
          Style
          <select
            value={style}
            data-testid={refsTestIds.bibStyle}
            onChange={(event) => setPicked(event.target.value as CitationStyle)}
            className="h-[var(--control-height)] rounded-sm border border-border bg-surface px-2 text-sm text-text"
          >
            {citationStyles.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </label>
        <span className="flex-1" />
        <ExportMenu bookId={bookId} />
      </div>
      {message !== null && (
        <p role="alert" data-testid={refsTestIds.bibError} className="m-0 text-base text-danger">
          {message}
        </p>
      )}
      {formatted?.entries && (
        <ol className="m-0 flex list-none flex-col gap-2 rounded-sm border border-border bg-surface p-6 pl-6">
          {formatted.entries.map((entry) => (
            <li
              key={entry.sourceId}
              data-testid={refsTestIds.bibEntry}
              data-source-id={entry.sourceId}
              data-status={entry.status}
              className={`text-base text-text ${entry.status === 'risk' ? 'underline decoration-danger decoration-2 underline-offset-4' : ''}`}
            >
              <span dangerouslySetInnerHTML={{ __html: entry.html }} />
              {entry.creditLine && (
                <span data-testid={refsTestIds.bibCredit} className="block text-sm text-muted">
                  {entry.creditLine}
                </span>
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
