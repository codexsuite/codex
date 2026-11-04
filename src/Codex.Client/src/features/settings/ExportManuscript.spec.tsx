import { expect, test } from '@playwright/experimental-ct-react';
import type { Page } from 'playwright';
import type {
  ExportJob,
  ManuscriptExportInfo,
  ManuscriptExportRequest,
} from '@codex/core/api/exports';
import { ExportManuscriptDialog } from './ExportManuscriptDialog';
import { ManuscriptJobs } from './ManuscriptJobs';
import { settingsTestIds as ids } from './testIds';

const book = '0199a000-0000-7000-8000-000000000012';
const chapter = '0199a000-0000-7000-8000-000000000c06';

const info: ManuscriptExportInfo = {
  chapters: [
    { id: '0199a000-0000-7000-8000-000000000c01', title: 'Preface', number: null },
    { id: chapter, title: 'Discovery is Everyone’s Job', number: 6 },
  ],
  atRiskSources: 3,
};

async function stub(page: Page, found: ManuscriptExportInfo = info) {
  const requests: ManuscriptExportRequest[] = [];
  await page.route(`**/api/books/${book}/manuscript-export`, (route) =>
    route.fulfill({ json: found }),
  );
  await page.route(`**/api/books/${book}/manuscript-exports`, (route) => {
    requests.push(route.request().postDataJSON() as ManuscriptExportRequest);
    return route.fulfill({ status: 202, json: {} });
  });
  return requests;
}

const job = (patch: Partial<ExportJob>): ExportJob => ({
  id: '0199a000-0000-7000-8000-0000000000e1',
  kind: 'docx',
  status: 'queued',
  step: null,
  progress: null,
  template: null,
  templateVersion: null,
  trim: null,
  bleed: null,
  cropMarks: false,
  quality: null,
  draft: false,
  pageCount: null,
  bytes: null,
  error: null,
  log: null,
  blocked: 0,
  warnings: 0,
  overfull: [],
  fileUrl: null,
  createdAt: '2026-10-19T09:00:00Z',
  createdLabel: 'Just now',
  finishedAt: null,
  ...patch,
});

test('Dialog_WhenSourcesAreAtRisk_ShouldSayTheExportIsNotGated', async ({ mount, page }) => {
  await stub(page);
  await mount(
    <ExportManuscriptDialog
      bookId={book}
      initialFormat="docx"
      busyFormats={[]}
      onClose={() => {}}
    />,
  );

  await expect(page.getByTestId(ids.manuscriptRisk)).toHaveText(
    '3 sources are at risk. This export is not gated.',
  );
  await expect(page.getByTestId(ids.manuscriptFormat('docx'))).toHaveAttribute(
    'aria-checked',
    'true',
  );
  await expect(page.getByTestId(ids.manuscriptSubmit)).toBeEnabled();
});

test('Dialog_WhenExported_ShouldSendTheChosenFormatScopeAndToggles', async ({ mount, page }) => {
  const requests = await stub(page);
  let closed = false;
  await mount(
    <ExportManuscriptDialog
      bookId={book}
      initialFormat="docx"
      busyFormats={[]}
      onClose={() => {
        closed = true;
      }}
    />,
  );
  await page.getByTestId(ids.manuscriptFormat('epub')).click();
  await page.getByTestId(ids.manuscriptScope).selectOption('chapter');
  await expect(page.getByTestId(ids.manuscriptSubmit)).toBeDisabled();
  await page.getByTestId(ids.manuscriptChapter).selectOption(chapter);
  await page.getByTestId(ids.manuscriptBeats).check();
  await page.getByTestId(ids.manuscriptSummaries).check();

  await page.getByTestId(ids.manuscriptSubmit).click();

  await expect.poll(() => closed).toBe(true);
  expect(requests).toEqual([
    {
      format: 'epub',
      scope: 'chapter',
      chapterId: chapter,
      includeBeats: true,
      includeSummaries: true,
    },
  ]);
});

test('Dialog_WhenTheBookHasNoChapters_ShouldSayNothingToExport', async ({ mount, page }) => {
  await stub(page, { chapters: [], atRiskSources: 0 });
  await mount(
    <ExportManuscriptDialog
      bookId={book}
      initialFormat="markdown"
      busyFormats={[]}
      onClose={() => {}}
    />,
  );

  await expect(page.getByTestId(ids.manuscriptError)).toHaveText(
    'Nothing to export. This book has no chapters.',
  );
  await expect(page.getByTestId(ids.manuscriptSubmit)).toBeDisabled();
});

test('Dialog_WhenTheFormatIsAlreadyRunning_ShouldDisableExport', async ({ mount, page }) => {
  await stub(page);
  await mount(
    <ExportManuscriptDialog
      bookId={book}
      initialFormat="epub"
      busyFormats={['epub']}
      onClose={() => {}}
    />,
  );

  await expect(page.getByTestId(ids.manuscriptBusy)).toHaveText('An export is already running');
  await expect(page.getByTestId(ids.manuscriptSubmit)).toBeDisabled();
  await page.getByTestId(ids.manuscriptFormat('docx')).click();
  await expect(page.getByTestId(ids.manuscriptSubmit)).toBeEnabled();
});

test('Jobs_WhenDoneFailedOrRunning_ShouldShowTheirStateDownloadAndRetry', async ({
  mount,
  page,
}) => {
  const retried: string[] = [];
  await mount(
    <ManuscriptJobs
      jobs={[
        job({
          id: '0199a000-0000-7000-8000-0000000000e3',
          kind: 'markdown',
          status: 'succeeded',
          filename: 'players-guide.md',
          fileUrl: '/api/exports/e3/file',
          notes: ['1 raw-LaTeX block skipped'],
        }),
        job({
          id: '0199a000-0000-7000-8000-0000000000e2',
          kind: 'epub',
          status: 'failed',
          error: 'Pandoc fell over.',
        }),
        job({ status: 'running' }),
      ]}
      onRetry={(format) => retried.push(format)}
    />,
  );
  const rows = page.getByTestId(ids.manuscriptJob);

  await expect(rows).toHaveCount(3);
  await expect(rows.nth(0).getByTestId(ids.manuscriptJobStatus)).toHaveText(
    'Done · players-guide.md',
  );
  await expect(rows.nth(0).getByTestId(ids.manuscriptDownload)).toHaveAttribute(
    'href',
    '/api/exports/e3/file',
  );
  await expect(rows.nth(0).getByTestId(ids.manuscriptJobNotes)).toHaveText(
    '1 raw-LaTeX block skipped',
  );
  await expect(rows.nth(1).getByTestId(ids.manuscriptJobStatus)).toHaveText('Failed');
  await expect(rows.nth(1).getByTestId(ids.manuscriptJobError)).toHaveText('Pandoc fell over.');
  await expect(rows.nth(2).getByTestId(ids.manuscriptJobStatus)).toHaveText('Running');

  await rows.nth(1).getByTestId(ids.manuscriptRetry).click();

  expect(retried).toEqual(['epub']);
});
