import { test, expect } from '@playwright/experimental-ct-react';
import { AppRoutes } from './AppRoutes';
import { testIds } from './testIds';

test('renders the app shell at the root route', async ({ mount, page }) => {
  await mount(<AppRoutes />, { hooksConfig: { route: '/' } });
  await expect(page.getByTestId(testIds.shell.appShell)).toBeVisible();
});
