import { expect } from '@playwright/experimental-ct-react';
import type { Locator, Page } from 'playwright';

/**
 * Tabs forward then backward through an open modal and checks focus never lands on the page behind it. A native
 * `<dialog>` can hand focus to the browser's own chrome, where nothing in the page has it (the body); every other
 * modal must keep it on one of its own controls.
 */
export async function expectFocusTrapped(page: Page, dialog: Locator, presses = 14) {
  for (const key of ['Tab', 'Shift+Tab']) {
    for (let press = 0; press < presses; press++) {
      await page.keyboard.press(key);
      const where = await dialog.evaluate((element) => {
        const active = document.activeElement;
        if (active === document.body) return 'body';
        return element.contains(active) ? 'inside' : 'outside';
      });
      expect(where, `after ${key} #${press + 1}`).not.toBe('outside');
    }
  }
}

/** Focus is on a control that is in the dialog, not on the body: the dialog took it when it opened. */
export async function expectFocusInside(dialog: Locator) {
  await expect
    .poll(() => dialog.evaluate((element) => element.contains(document.activeElement)))
    .toBe(true);
}
