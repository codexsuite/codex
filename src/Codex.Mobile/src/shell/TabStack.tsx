import { Stack } from 'expo-router';
import { BookBar } from './BookBar';
import { useTheme } from '../theme';

/**
 * The stack inside one book tab: the tab's `index` screen under the book bar, and anything pushed on the tab (an entry,
 * the style guide) full-screen with its own `‹ Parent` bar (`PushBar`) while the tab stays selected.
 */
export function TabStack() {
  const theme = useTheme();
  return (
    <Stack
      screenOptions={{ headerShown: false, contentStyle: { backgroundColor: theme.colors.bg } }}
    >
      <Stack.Screen name="index" options={{ headerShown: true, header: () => <BookBar /> }} />
    </Stack>
  );
}
