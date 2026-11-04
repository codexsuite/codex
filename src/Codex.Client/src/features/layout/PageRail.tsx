import { layoutTestIds } from './testIds';

// One button per page, with a warn dot on the pages that carry a print issue; a click scrolls to the page.
export function PageRail({ pages, issuePages }: { pages: number; issuePages: number[] }) {
  const go = (n: number) => {
    const all = document.querySelectorAll('.layout-live .pagedjs_page');
    all[n - 1]?.scrollIntoView({ block: 'start' });
  };
  return (
    <nav
      aria-label="Pages"
      data-testid={layoutTestIds.pageRail}
      className="flex w-12 shrink-0 flex-col items-center gap-1 overflow-auto border-r border-border bg-surface py-2"
    >
      {Array.from({ length: pages }, (_, i) => i + 1).map((n) => (
        <button
          key={n}
          type="button"
          data-testid={layoutTestIds.railPage(n)}
          onClick={() => go(n)}
          className="relative h-8 w-8 rounded-sm border border-border bg-bg text-xs"
        >
          {n}
          {issuePages.includes(n) && (
            <span
              className="absolute -top-1 -right-1 h-2 w-2 rounded-full bg-warn"
              aria-label="Print issue"
            />
          )}
        </button>
      ))}
    </nav>
  );
}
