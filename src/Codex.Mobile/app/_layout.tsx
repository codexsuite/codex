import { QueryClientProvider } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { configureMobilePlatform } from '~/mobilePlatform';
import { queryClient } from '~/queryClient';
import { useSession } from '~/session/session';
import { sheetScreenOptions } from '~/shell/sheetOptions';
import { useTheme } from '~/theme';

// The core reads storage and the server address through the platform: configure it before anything renders.
configureMobilePlatform();

export default function RootLayout() {
  const theme = useTheme();
  // The session is read synchronously when the app starts (session.ts), so the first render already knows which side
  // of the gate the user is on. A lost session clears the secret, which swaps the Library for the sign-in screen.
  const signedIn = useSession().secret !== null;

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <StatusBar style={theme.scheme === 'dark' ? 'light' : 'dark'} />
        <Stack
          screenOptions={{ headerShown: false, contentStyle: { backgroundColor: theme.colors.bg } }}
        >
          <Stack.Protected guard={signedIn}>
            <Stack.Screen name="index" />
            <Stack.Screen name="book/[bookId]" />
            <Stack.Screen name="speech-models" />
            <Stack.Screen name="account" options={sheetScreenOptions} />
            <Stack.Screen name="book-switcher" options={sheetScreenOptions} />
            <Stack.Screen name="record" options={sheetScreenOptions} />
          </Stack.Protected>
          <Stack.Protected guard={!signedIn}>
            <Stack.Screen name="login" />
          </Stack.Protected>
        </Stack>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}
