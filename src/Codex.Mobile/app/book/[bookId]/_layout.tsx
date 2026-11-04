import { Stack } from 'expo-router';
import { sheetScreenOptions } from '~/shell/sheetOptions';
import { useTheme } from '~/theme';

// A book: its tabs, and the sheets that open over whichever tab you are on (the scene sheet). The tabs are in `(tabs)/`.
export default function BookLayout() {
  const theme = useTheme();
  return (
    <Stack
      screenOptions={{ headerShown: false, contentStyle: { backgroundColor: theme.colors.bg } }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="scene/[sceneId]" options={sheetScreenOptions} />
      <Stack.Screen name="chapters" options={sheetScreenOptions} />
    </Stack>
  );
}
