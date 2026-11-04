import { useRulesImpact } from '../../api/settingsQueries';
import { referencesLink } from '@codex/core/shell/links';
import { Link } from 'react-router-dom';
import { settingsTestIds } from './testIds';

const scaleMax = 400;
const position = (words: number) => `${Math.min(100, (words / scaleMax) * 100)}%`;

// A strip of each cited source's quoted words against the limit being typed (danger above it), then the sources at
// risk and why. Nothing is written: it asks the server what that limit would do.
export function RulesImpactStrip({ bookId, limit }: { bookId: string; limit: number | null }) {
  const impact = useRulesImpact(bookId, limit);
  if (limit === null) return null;
  if (impact.isError) {
    return (
      <p data-testid={settingsTestIds.impactError} role="alert" className="m-0 text-sm text-danger">
        {impact.error.message}
      </p>
    );
  }
  const data = impact.data;
  if (!data) return null;
  const quoted = data.sources.filter((s) => s.quotedWords > 0);
  const atRisk = data.sources.filter((s) => s.atRisk);
  return (
    <div
      data-testid={settingsTestIds.impact}
      data-limit={data.limit}
      className="flex w-full basis-full flex-col gap-2 rounded border border-border bg-bg p-2 text-sm"
    >
      <b
        data-testid={settingsTestIds.impactSummary}
        className={atRisk.length ? 'text-danger' : 'text-text'}
      >
        {atRisk.length === 0
          ? 'No sources at risk'
          : `${atRisk.length} ${atRisk.length === 1 ? 'source' : 'sources'} at copyright risk, blocking print export`}
      </b>
      <div aria-hidden="true" className="relative mb-4 mt-2 h-3">
        <span className="absolute inset-x-0 top-1/2 h-px bg-border" />
        {quoted.map((source) => (
          <span
            key={source.referenceId}
            data-testid={settingsTestIds.impactDot(source.referenceId)}
            data-over={source.quotedWords > data.limit}
            title={`${source.title}: ${source.quotedWords} words`}
            style={{ left: position(source.quotedWords) }}
            className={`absolute top-0 size-3 -translate-x-1/2 rounded-full ${source.quotedWords > data.limit ? 'bg-danger' : 'bg-ok'}`}
          />
        ))}
        <span
          title={`Limit: ${data.limit} words`}
          style={{ left: position(data.limit) }}
          className="absolute -top-1 h-5 w-0.5 bg-text"
        />
      </div>
      <ul className="m-0 flex list-none flex-col gap-1 p-0">
        {atRisk.map((source) => (
          <li
            key={source.referenceId}
            data-testid={settingsTestIds.impactSource(source.referenceId)}
            className="flex flex-wrap items-center gap-2"
          >
            <span className="text-2xs uppercase tracking-wide text-danger">Copyright risk</span>
            <span className="text-text">{source.title}</span>
            <span className="text-muted">{source.why}</span>
          </li>
        ))}
      </ul>
      <Link to={referencesLink(bookId)} className="text-xs text-accent">
        Open References
      </Link>
    </div>
  );
}
