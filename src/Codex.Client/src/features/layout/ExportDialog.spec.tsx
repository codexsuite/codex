import { expect, test } from '@playwright/experimental-ct-react';
import type { ExportRequest, Preflight } from '@codex/core/api/exports';
import type { LayoutState } from '@codex/core/api/layout';
import { expectFocusInside, expectFocusTrapped } from '../../test-support/dialogFocus';
import { ExportDialog } from './ExportDialog';
import { layoutTestIds } from './testIds';

const layout: LayoutState = {
  template: 'novel-6x9',
  trim: '6x9',
  templates: [
    {
      slug: 'novel-6x9',
      name: 'Novel (6 x 9)',
      version: '1.0.0',
      engine: 'tectonic',
      trims: ['6x9'],
      columns: 1,
      hasPreviewCss: true,
      notices: [],
      options: [],
    },
  ],
};

const blocked: Preflight = {
  verdict: 'blocked',
  blocks: [
    {
      severity: 'block',
      kind: 'copyright_risk',
      detail: 'The Velocity Trap: copyright risk (quote over limit)',
      sceneId: null,
      referenceId: 'r1',
    },
  ],
  warnings: [
    {
      severity: 'warn',
      kind: 'low_dpi',
      detail: 'Low resolution: map.png',
      sceneId: null,
      referenceId: null,
    },
  ],
  notes: [],
  template: 'novel-6x9',
  templateVersion: '1.0.0',
  trim: '6x9',
  estimatedMegabytes: 12,
};

const ready: Preflight = { ...blocked, verdict: 'ready', blocks: [] };

const initial: ExportRequest = {
  bleed: 'none',
  cropMarks: false,
  quality: 'print',
  format: 'pdf',
  draft: false,
};

async function open(
  mount: Parameters<Parameters<typeof test>[2]>[0]['mount'],
  page: Parameters<Parameters<typeof test>[2]>[0]['page'],
  preflight: Preflight,
  queued: ExportRequest[],
) {
  await page.route('**/api/books/b/export/preflight', (route) =>
    route.fulfill({ json: preflight }),
  );
  await mount(
    <ExportDialog
      bookId="b"
      layout={layout}
      initial={initial}
      sceneInfo={() => undefined}
      onClose={() => undefined}
      onQueue={(request) => {
        queued.push(request);
        return Promise.resolve(null);
      }}
    />,
  );
}

test('ExportDialog_WhenPreflightBlocks_ShouldDisableQueueUntilDraftIsChosen', async ({
  mount,
  page,
}) => {
  const queued: ExportRequest[] = [];
  await open(mount, page, blocked, queued);

  await expect(page.getByTestId(layoutTestIds.exportVerdict)).toHaveText('1 issue blocks export');
  await expect(page.getByTestId(layoutTestIds.exportQueue)).toBeDisabled();
  await page.getByTestId(layoutTestIds.exportDraft).check();
  await expect(page.getByTestId(layoutTestIds.exportQueue)).toBeEnabled();
  await page.getByTestId(layoutTestIds.exportQueue).click();

  expect(queued).toHaveLength(1);
  expect(queued[0]).toMatchObject({ draft: true, template: 'novel-6x9', trim: '6x9' });
});

test('ExportDialog_WhenReady_ShouldOfferNoDraftAndSendBleedCropMarksAndQuality', async ({
  mount,
  page,
}) => {
  const queued: ExportRequest[] = [];
  await open(mount, page, ready, queued);

  await expect(page.getByTestId(layoutTestIds.exportVerdict)).toHaveText('Ready to export');
  await expect(page.getByTestId(layoutTestIds.exportDraft)).toHaveCount(0);
  await expect(page.getByTestId(layoutTestIds.exportCrop)).toBeDisabled();
  await page.getByTestId(layoutTestIds.exportBleed('3mm')).click();
  await page.getByTestId(layoutTestIds.exportCrop).check();
  await page.getByTestId(layoutTestIds.exportQuality('screen')).click();
  await page.getByTestId(layoutTestIds.exportQueue).click();

  expect(queued[0]).toMatchObject({
    bleed: '3mm',
    cropMarks: true,
    quality: 'screen',
    format: 'pdf',
    draft: false,
  });
});

test('ExportDialog_WhenTheServerRefuses_ShouldShowWhyAndStayOpen', async ({ mount, page }) => {
  await page.route('**/api/books/b/export/preflight', (route) => route.fulfill({ json: ready }));
  await mount(
    <ExportDialog
      bookId="b"
      layout={layout}
      initial={initial}
      sceneInfo={() => undefined}
      onClose={() => undefined}
      onQueue={() => Promise.resolve('An export of this book is already running.')}
    />,
  );

  await page.getByTestId(layoutTestIds.exportQueue).click();

  await expect(page.getByTestId(layoutTestIds.exportError)).toHaveText(
    'An export of this book is already running.',
  );
});

test('ExportDialog_WhenOpen_ShouldTakeFocusAndKeepTabInsideIt', async ({ mount, page }) => {
  await open(mount, page, ready, []);
  const dialog = page.getByTestId(layoutTestIds.exportDialog);

  await expect(dialog).toBeVisible();
  await expectFocusInside(dialog);
  await expectFocusTrapped(page, dialog);
});
