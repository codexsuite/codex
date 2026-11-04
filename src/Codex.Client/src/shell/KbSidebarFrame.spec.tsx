import { expect, test } from '@playwright/experimental-ct-react';
import type { KbSidebarSection } from '@codex/core/features/kb/kbSidebar';
import { KbSidebarFrame } from './KbSidebarFrame';
import { entryDragType } from './dragPayload';
import { shellTestIds } from './shellTestIds';
import { testIds } from '../testIds';
import { kbTestIds } from '../features/kb/testIds';
import type { Kb } from '@codex/core/api/kb';
import { KbSidebarHarness } from '../test-support/KbSidebarHarness';

const sections: KbSidebarSection[] = [
  {
    key: 'book',
    seriesName: null,
    groups: [
      {
        id: 'c:concepts',
        name: 'Concepts',
        colour: null,
        entries: [{ id: 'e:velocity', name: 'Velocity', sceneCount: 2 }],
      },
      {
        id: 'c:people',
        name: 'People',
        colour: 'category-person',
        entries: [{ id: 'e:ada', name: 'Ada', sceneCount: 0 }],
      },
    ],
  },
];

test('KbSidebarFrame_WithEntries_ShouldRenderAndExposeDragPayload', async ({ mount, page }) => {
  await mount(<KbSidebarFrame sections={sections} totalEntries={2} />);
  await expect(page.getByTestId(testIds.shell.kbItem('e:velocity'))).toContainText('Velocity');
  await expect(page.getByTestId(testIds.shell.kbItem('e:ada'))).toBeVisible();
  await expect(page.getByTestId(shellTestIds.kbEmpty)).toHaveCount(0);

  const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
  await page
    .getByTestId(testIds.shell.kbItem('e:ada'))
    .dispatchEvent('dragstart', { dataTransfer });
  const payload = await dataTransfer.evaluate((dt, type) => dt.getData(type), entryDragType);
  expect(JSON.parse(payload)).toEqual({ entryId: 'e:ada', name: 'Ada' });
});

test('KbSidebarFrame_WithNoEntries_ShouldShowEmptyState', async ({ mount, page }) => {
  await mount(<KbSidebarFrame sections={[]} totalEntries={0} />);
  await expect(page.getByTestId(shellTestIds.kbEmpty)).toBeVisible();
});

const category = (id: string, name: string, position: string) => ({
  id,
  name,
  tag: null,
  colour: 'category-person',
  position,
  fieldDefs: [],
  fromSeries: false,
  seriesName: null,
});
const entry = (id: string, categoryId: string, name: string) => ({
  id,
  categoryId,
  name,
  aliases: [],
  sceneCount: 1,
  useCount: 1,
  fromSeries: false,
  seriesName: null,
  alwaysInContext: false,
  summaryLine: '',
});
const kb: Kb = {
  categories: [category('c:people', 'People', 'a0'), category('c:places', 'Places', 'a1')],
  entries: [entry('e:ada', 'c:people', 'Ada'), entry('e:harbour', 'c:places', 'Harbour')],
};

test('KbSidebarFrame_WhenCategoryUnchecked_ShouldHideItsEntriesUntilShowAll', async ({
  mount,
  page,
}) => {
  await mount(<KbSidebarHarness kb={kb} />);
  const filter = page.getByTestId(kbTestIds.filter);
  await expect(filter).toHaveAttribute('data-active', 'false');
  await expect(filter).toHaveAttribute('aria-label', 'Filter Knowledge base');

  await filter.click();
  await expect(page.getByTestId(kbTestIds.filterOption('c:people'))).toBeChecked();
  await expect(page.getByTestId(kbTestIds.filterShowAll)).toBeDisabled();
  await page.getByTestId(kbTestIds.filterOption('c:places')).uncheck();

  await expect(page.getByTestId(kbTestIds.category('c:places'))).toHaveCount(0);
  await expect(page.getByTestId(testIds.shell.kbItem('e:ada'))).toBeVisible();
  await expect(filter).toHaveAttribute('data-active', 'true');
  await expect(filter).toHaveAttribute('aria-label', 'Filter Knowledge base (1 category hidden)');

  await page.getByTestId(kbTestIds.filterShowAll).click();
  await expect(page.getByTestId(testIds.shell.kbItem('e:harbour'))).toBeVisible();
  await expect(filter).toHaveAttribute('data-active', 'false');
});

test('KbSidebarFrame_WhenEveryCategoryHidden_ShouldShowNoMatch', async ({ mount, page }) => {
  await mount(<KbSidebarHarness kb={kb} />);
  await page.getByTestId(kbTestIds.filter).click();
  await page.getByTestId(kbTestIds.filterOption('c:people')).uncheck();
  await page.getByTestId(kbTestIds.filterOption('c:places')).uncheck();

  await expect(page.getByTestId(kbTestIds.noMatch)).toBeVisible();
  await expect(page.getByTestId(kbTestIds.newEntryButton)).toBeDisabled();
});

test('KbSidebarFrame_FilterPanel_ShouldCloseOnEscapeAndOutsideClick', async ({ mount, page }) => {
  await mount(<KbSidebarHarness kb={kb} />);
  await page.getByTestId(kbTestIds.filter).click();
  await expect(page.getByTestId(kbTestIds.filterPanel)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByTestId(kbTestIds.filterPanel)).toHaveCount(0);

  await page.getByTestId(kbTestIds.filter).click();
  await page.getByTestId(kbTestIds.search).click();
  await expect(page.getByTestId(kbTestIds.filterPanel)).toHaveCount(0);
});
