import { TabStack } from '~/shell/TabStack';

// A cold link to a pushed screen (Continue card, deep link) gets this tab's list under it, so ‹ goes back to the list.
export const unstable_settings = { anchor: 'index' };

export default function Layout() {
  return <TabStack />;
}
