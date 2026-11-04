import { Tabs, usePathname } from 'expo-router';
import { useEffect } from 'react';
import { lastScreenMemory, lastScreenOfPath } from '~/book/lastScreen';
import { TabBar } from '~/shell/TabBar';

// The book's bottom tabs: Plan, Write, Knowledge, Settings. Records where in the book you are, for the Library's Continue card.
export default function BookTabsLayout() {
  const pathname = usePathname();
  useEffect(() => {
    const here = lastScreenOfPath(pathname);
    if (here) lastScreenMemory.remember(here.bookId, here.screen);
  }, [pathname]);

  return (
    <Tabs tabBar={(props) => <TabBar {...props} />} screenOptions={{ headerShown: false }}>
      <Tabs.Screen name="plan" options={{ title: 'Plan' }} />
      <Tabs.Screen name="write" options={{ title: 'Write' }} />
      <Tabs.Screen name="knowledge" options={{ title: 'Knowledge' }} />
      <Tabs.Screen name="settings" options={{ title: 'Settings' }} />
    </Tabs>
  );
}
