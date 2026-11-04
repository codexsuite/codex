import { Link } from 'react-router-dom';
import type { Preflight, PreflightItem } from '@codex/core/api/exports';
import { referencesLink, writeLink } from '@codex/core/shell/links';
import type { SceneInfo } from './sceneLabels';
import { layoutTestIds } from './testIds';

interface PreflightListProps {
  bookId: string;
  preflight: Preflight;
  sceneInfo: (sceneId: string) => SceneInfo | undefined;
}

const GROUPS = [
  { key: 'blocks', title: 'Blocks export', tone: 'text-danger' },
  { key: 'warnings', title: 'Warnings', tone: 'text-warn' },
  { key: 'notes', title: 'Notes', tone: 'text-muted' },
] as const;

// The verdict and the findings under Blocks export, Warnings and Notes; each says where it is, with Open in Write for a scene
// and Open in References for a copyright risk.
export function PreflightList({ bookId, preflight, sceneInfo }: PreflightListProps) {
  const blocked = preflight.verdict === 'blocked';
  return (
    <div data-testid={layoutTestIds.preflightList} data-verdict={preflight.verdict}>
      <p
        data-testid={layoutTestIds.exportVerdict}
        className={`m-0 mb-2 font-semibold ${blocked ? 'text-danger' : 'text-ok'}`}
      >
        {blocked
          ? `${preflight.blocks.length} ${preflight.blocks.length === 1 ? 'issue blocks' : 'issues block'} export`
          : 'Ready to export'}
      </p>
      {GROUPS.map(({ key, title, tone }) => {
        const items: PreflightItem[] = preflight[key];
        if (items.length === 0) return null;
        return (
          <section key={key} className="mb-3">
            <h4 className={`m-0 mb-1 text-xs font-semibold ${tone}`}>
              {title} ({items.length})
            </h4>
            <ul className="m-0 list-none p-0">
              {items.map((item, i) => (
                <PreflightRow
                  key={`${item.kind}-${item.sceneId ?? item.referenceId ?? ''}-${i}`}
                  bookId={bookId}
                  item={item}
                  scene={item.sceneId === null ? undefined : sceneInfo(item.sceneId)}
                />
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function PreflightRow(props: {
  bookId: string;
  item: PreflightItem;
  scene: SceneInfo | undefined;
}) {
  const { item, scene, bookId } = props;
  return (
    <li
      data-testid={layoutTestIds.preflightItem}
      data-kind={item.kind}
      data-severity={item.severity}
      className="mb-2 text-xs"
    >
      <div>{item.detail}</div>
      <div className="flex gap-3">
        {scene && <span className="text-muted">{scene.label}</span>}
        {scene && (
          <Link className="text-accent" to={writeLink(bookId, { scene: scene.number })}>
            Open in Write
          </Link>
        )}
        {item.referenceId !== null && (
          <Link className="text-accent" to={referencesLink(bookId, { source: item.referenceId })}>
            Open in References
          </Link>
        )}
      </div>
    </li>
  );
}
