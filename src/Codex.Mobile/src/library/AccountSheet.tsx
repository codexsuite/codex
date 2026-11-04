import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { speechModelsLink } from '~/links';
import { signOutOfServer } from '~/session/auth';
import { useSession } from '~/session/session';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { ErrorState, Row, Sheet } from '~/ui';
import { serverLine } from './serverLine';
import { useHealth } from './useHealth';
import { libraryTestIds } from './testIds';
import { useMe } from './useMe';

/** The Account sheet: who is signed in, to which server, Speech models and Sign out. */
export function AccountSheet() {
  const { styles } = useStyles(makeStyles);
  const me = useMe();
  const { origin } = useSession();
  const health = useHealth(origin);
  const [signingOut, setSigningOut] = useState(false);

  async function signOut() {
    setSigningOut(true);
    try {
      // Clearing the session swaps the signed-in screens for Sign in, with the server and email kept.
      await signOutOfServer();
    } finally {
      setSigningOut(false);
    }
  }

  return (
    <Sheet title="Account">
      <View testID={libraryTestIds.accountSheet}>
        {me.isError && (
          <ErrorState
            error={me.error}
            fallback="Couldn't load your account."
            onRetry={() => void me.refetch()}
            testID={`${libraryTestIds.accountSheet}-error`}
          />
        )}
        {me.data && (
          <View style={styles.who}>
            <View style={styles.avatar}>
              <Text style={styles.initials}>{me.data.initials}</Text>
            </View>
            <View style={styles.names}>
              <Text testID={libraryTestIds.accountName} style={styles.name}>
                {me.data.displayName}
              </Text>
              <Text testID={libraryTestIds.accountEmail} style={styles.email}>
                {me.data.email}
              </Text>
            </View>
          </View>
        )}
        <Row
          testID={libraryTestIds.accountServer}
          label="Server"
          detail={origin === null ? 'Not set' : serverLine(origin, health.data?.version ?? null)}
        />
        <Row
          testID={libraryTestIds.accountSpeech}
          label="Speech models"
          onPress={() => router.push(speechModelsLink())}
        />
        <Row
          testID={libraryTestIds.accountSignOut}
          label="Sign out"
          detail="Back to Sign in, with this server filled in"
          danger
          disabled={signingOut}
          onPress={() => void signOut()}
        />
        <Text style={styles.foot}>
          To use another server, sign out and change the address. Password and users are managed on
          desktop.
        </Text>
      </View>
    </Sheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    who: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[3],
      paddingHorizontal: theme.space[4],
      paddingBottom: theme.space[3],
    },
    avatar: {
      width: 40,
      height: 40,
      borderRadius: 20,
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: theme.colors['accent-soft'],
    },
    initials: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.semibold,
      color: theme.colors.accent,
    },
    names: { flex: 1, minWidth: 0 },
    name: {
      fontSize: theme.text.base,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
    },
    email: { fontSize: theme.text.sm, color: theme.colors.muted },
    foot: {
      padding: theme.space[4],
      fontSize: theme.text.xs,
      color: theme.colors.muted,
    },
  });
}
