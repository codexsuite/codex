import { expect, test } from '@playwright/experimental-ct-react';
import type { Proof } from '@codex/core/api/proofs';
import { ProofBanner } from './ProofBanner';
import { layoutTestIds } from './testIds';

const proof: Proof = {
  id: 'p1',
  chapterId: 'c1',
  status: 'succeeded',
  step: 'Done',
  progress: 1,
  cacheKey: 'k',
  templateSlug: 'novel-6x9',
  templateVersion: '1.0.0',
  trim: '6x9',
  pageCount: 12,
  error: null,
  log: null,
  cached: false,
  pdfUrl: '/api/proofs/p1/pdf',
  createdAt: '2026-10-21T05:00:00Z',
  finishedAt: '2026-10-21T05:00:04Z',
};

const noop = () => undefined;

test('says it is one chapter and offers Download, Log and Re-render', async ({ mount }) => {
  const component = await mount(
    <ProofBanner
      proof={proof}
      cached={false}
      outOfDate={false}
      logOpen={false}
      onLog={noop}
      onRerender={noop}
    />,
  );
  await expect(component).toContainText('chapter only, no contents or cross-references');
  await expect(component).toContainText('novel-6x9 v1.0.0');
  await expect(component.getByTestId(layoutTestIds.proofDownload)).toHaveAttribute(
    'href',
    '/api/proofs/p1/pdf',
  );
  await expect(component.getByTestId(layoutTestIds.proofCached)).toHaveCount(0);
  await expect(component.getByTestId(layoutTestIds.proofOutOfDate)).toHaveCount(0);
});

test('marks a cached and an out-of-date proof, and Re-render and Log call back', async ({
  mount,
}) => {
  const calls: string[] = [];
  const component = await mount(
    <ProofBanner
      proof={proof}
      cached
      outOfDate
      logOpen
      onLog={() => calls.push('log')}
      onRerender={() => calls.push('rerender')}
    />,
  );
  await expect(component.getByTestId(layoutTestIds.proofCached)).toBeVisible();
  await expect(component.getByTestId(layoutTestIds.proofOutOfDate)).toBeVisible();
  await expect(component.getByTestId(layoutTestIds.proofLogToggle)).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await component.getByTestId(layoutTestIds.proofRerender).click();
  await component.getByTestId(layoutTestIds.proofLogToggle).click();
  expect(calls).toEqual(['rerender', 'log']);
});

test('has no Download link for a proof without a PDF', async ({ mount }) => {
  const component = await mount(
    <ProofBanner
      proof={{ ...proof, status: 'failed', pdfUrl: null }}
      cached={false}
      outOfDate={false}
      logOpen
      onLog={noop}
      onRerender={noop}
    />,
  );
  await expect(component.getByTestId(layoutTestIds.proofDownload)).toHaveCount(0);
});
