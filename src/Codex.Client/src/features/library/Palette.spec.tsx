import { expect, test } from '@playwright/experimental-ct-react';
import library from '../../../../Codex.TestSupport/Fixtures/library.json' with { type: 'json' };
import { AppRoutes } from '../../AppRoutes';
import { stubShellApi } from '../../test-support/shellStubs';
import { shellTestIds } from '../../shell/shellTestIds';
import { testIds } from '../../testIds';
import { libraryTestIds } from './testIds';

type Mount = (c: React.JSX.Element, o: { hooksConfig: { route: string } }) => Promise<unknown>;

const northbound = library.standalone[0];
const series = library.series[0];

async function openPalette(page: Parameters<typeof stubShellApi>[0], mount: Mount, route = '/') {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route } });
  // The Library bar has the ⌘K key cap; the book bar does not, so a book route waits for the bar itself.
  await expect(
    page.getByTestId(route === '/' ? shellTestIds.paletteButton : testIds.shell.bookBar),
  ).toBeVisible();
  await page.keyboard.press('Control+k');
  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeVisible();
}

test('Palette_WhenTyping_ShouldFilterBooksSeriesAndActions', async ({ mount, page }) => {
  await openPalette(page, mount);
  await expect(page.getByTestId(libraryTestIds.paletteInput)).toBeFocused();
  await expect(page.getByTestId(libraryTestIds.paletteBook(northbound.id))).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.paletteSeries(series.id))).toBeVisible();

  await page.getByTestId(libraryTestIds.paletteInput).fill(northbound.title);

  await expect(page.getByTestId(libraryTestIds.paletteBook(northbound.id))).toBeVisible();
  await expect(page.getByTestId(libraryTestIds.paletteSeries(series.id))).toBeHidden();
  await expect(page.getByTestId(libraryTestIds.paletteAction('import'))).toBeHidden();
});

test('Palette_WhenNothingMatches_ShouldSayNothingMatches', async ({ mount, page }) => {
  await openPalette(page, mount);

  await page.getByTestId(libraryTestIds.paletteInput).fill('zzzzqqqq');

  await expect(page.getByTestId(libraryTestIds.paletteEmpty)).toBeVisible();
});

test('Palette_WhenEnterOnBook_ShouldOpenItAndClose', async ({ mount, page }) => {
  await openPalette(page, mount);

  await page.getByTestId(libraryTestIds.paletteInput).fill(northbound.title);
  await expect(page.getByTestId(libraryTestIds.paletteBook(northbound.id))).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await page.keyboard.press('Enter');

  await expect(page.getByTestId(testIds.shell.bookBar)).toBeVisible();
  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeHidden();
});

test('Palette_WhenEscape_ShouldClose', async ({ mount, page }) => {
  await openPalette(page, mount);

  await page.keyboard.press('Escape');

  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeHidden();
});

test('PaletteButton_WhenClickedOnLibrary_ShouldOpenThePalette', async ({ mount, page }) => {
  await stubShellApi(page, library);
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });

  await page.getByTestId(shellTestIds.paletteButton).click();

  await expect(page.getByTestId(testIds.shell.commandPalette)).toBeVisible();
});

test('Palette_WhenNewBookAction_ShouldOpenTheDialog', async ({ mount, page }) => {
  await openPalette(page, mount, `/books/${northbound.id}/plan`);

  await page.getByTestId(libraryTestIds.paletteAction('new-book')).click();

  await expect(page.getByTestId(libraryTestIds.newBookDialog)).toBeVisible();
});
