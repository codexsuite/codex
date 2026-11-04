import { expect, test } from '@playwright/experimental-ct-react';
import type { ExportJob, ExportList } from '@codex/core/api/exports';
import { ExportsTab } from './ExportsTab';
import { layoutTestIds } from './testIds';
import type { ExportSession } from './useExports';

const base: ExportJob = {
  id: 'e1',
  kind: 'print',
  status: 'succeeded',
  step: 'Done',
  progress: 1,
  template: 'novel-6x9',
  templateVersion: '1.0.0',
  trim: '6x9',
  bleed: '3mm',
  cropMarks: true,
  quality: 'print',
  draft: false,
  pageCount: 212,
  bytes: 52_428_800,
  error: null,
  log: null,
  blocked: 0,
  warnings: 2,
  overfull: [],
  fileUrl: '/api/exports/e1/file',
  createdAt: '2026-10-21T05:00:00Z',
  createdLabel: '2h ago',
  finishedAt: '2026-10-21T05:04:00Z',
};

function session(list: ExportList, cancelled: string[] = []): ExportSession {
  return {
    list,
    error: null,
    cancel: (id) => cancelled.push(id),
    queue: () => Promise.resolve(null),
  };
}

test('ExportsTab_WhenOneIsRunning_ShouldShowItsStepAndCancelIt', async ({ mount, page }) => {
  const cancelled: string[] = [];
  const running: ExportJob = {
    ...base,
    id: 'e2',
    status: 'running',
    step: 'Typesetting',
    progress: 0.4,
    fileUrl: null,
  };
  await mount(
    <ExportsTab
      session={session({ running: 'e2', exports: [running, base] }, cancelled)}
      onAgain={() => undefined}
    />,
  );

  await expect(page.getByTestId(layoutTestIds.exportSteps)).toHaveText('Typesetting · 40%');
  await expect(page.getByTestId(layoutTestIds.exportItem)).toHaveCount(1);
  await page.getByTestId(layoutTestIds.exportCancel).click();

  expect(cancelled).toEqual(['e2']);
});

test('ExportsTab_WhenAnExportIsDone_ShouldListItsSettingsDownloadAndExportAgain', async ({
  mount,
  page,
}) => {
  const again: ExportJob[] = [];
  await mount(
    <ExportsTab
      session={session({ running: null, exports: [base] })}
      onAgain={(job) => again.push(job)}
    />,
  );

  const item = page.getByTestId(layoutTestIds.exportItem);
  await expect(item).toHaveAttribute('data-status', 'succeeded');
  await expect(item).toContainText('novel-6x9 v1.0.0 · 6x9 · 3 mm bleed · crop marks · 300 dpi');
  await expect(page.getByTestId(layoutTestIds.exportDone)).toHaveText('Done · 212 pages · 50.0 MB');
  await expect(page.getByTestId(layoutTestIds.exportDownload)).toHaveAttribute(
    'href',
    '/api/exports/e1/file',
  );
  await page.getByTestId(layoutTestIds.exportAgain).click();

  expect(again.map((j) => j.id)).toEqual(['e1']);
});

test('ExportsTab_WhenAnExportFailed_ShouldShowWhyAndItsLog', async ({ mount, page }) => {
  const failed: ExportJob = {
    ...base,
    id: 'e3',
    status: 'failed',
    error: 'Typesetting failed; see the log.',
    fileUrl: null,
    pageCount: null,
    bytes: null,
  };
  await page.route('**/api/exports/e3', (route) =>
    route.fulfill({ json: { ...failed, log: '! Undefined control sequence.' } }),
  );
  await mount(
    <ExportsTab
      session={session({ running: null, exports: [failed] })}
      onAgain={() => undefined}
    />,
  );

  await expect(page.getByTestId(layoutTestIds.exportItem)).toContainText(
    'Typesetting failed; see the log.',
  );
  await page.getByTestId(layoutTestIds.exportLogToggle).click();

  await expect(page.getByTestId(layoutTestIds.exportLog)).toContainText(
    'Undefined control sequence',
  );
});

test('ExportsTab_WhenThereAreNone_ShouldSaySo', async ({ mount }) => {
  const component = await mount(
    <ExportsTab session={session({ running: null, exports: [] })} onAgain={() => undefined} />,
  );

  await expect(component).toContainText('No exports yet.');
});
