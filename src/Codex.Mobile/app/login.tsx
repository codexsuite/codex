import { useRef, useState, type ComponentRef } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { sessionStore } from '~/session/session';
import { signInToServer } from '~/session/auth';
import { canSignIn, checkLine } from '~/session/serverCheck';
import { useServerCheck } from '~/session/useServerCheck';
import { useTheme, type Theme } from '~/theme';

export default function Login() {
  const theme = useTheme();
  const styles = makeStyles(theme);
  const insets = useSafeAreaInsets();

  const [server, setServer] = useState(() => sessionStore.getState().origin ?? '');
  const [email, setEmail] = useState(() => sessionStore.getState().email);
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const { check, verify, reset } = useServerCheck(server, { checkOnMount: true });
  const line = checkLine(check);
  const ready = canSignIn(check, server);
  const emailRef = useRef<ComponentRef<typeof TextInput>>(null);
  const passwordRef = useRef<ComponentRef<typeof TextInput>>(null);

  async function submit() {
    if (busy) return;
    setBusy(true);
    setError(null);
    // Not checked yet (Return from the password field before leaving the server field): check now, then go on.
    if (!ready && !(await verify(server))) {
      setBusy(false);
      return;
    }
    const result = await signInToServer({ server, email, password });
    // On success the root layout's gate replaces this screen.
    if (!result.ok) {
      setError(result.error);
      setBusy(false);
    }
  }

  return (
    <KeyboardAvoidingView
      style={styles.fill}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        style={styles.fill}
        contentContainerStyle={[
          styles.content,
          {
            paddingTop: insets.top + theme.space[5],
            paddingBottom: insets.bottom + theme.space[5],
          },
        ]}
        keyboardShouldPersistTaps="handled"
      >
        <Text style={styles.title} accessibilityRole="header">
          Codex
        </Text>
        <Text style={styles.subtitle}>Sign in to your server</Text>

        <Text style={styles.label}>Server</Text>
        <TextInput
          testID="mobile-login-server"
          style={styles.input}
          value={server}
          onChangeText={(text) => {
            setServer(text);
            reset();
          }}
          onBlur={() => {
            if (!ready && server.trim() !== '') void verify(server);
          }}
          placeholder="codex.example.com"
          placeholderTextColor={theme.colors.muted}
          keyboardType="url"
          autoCapitalize="none"
          autoCorrect={false}
          returnKeyType="next"
          onSubmitEditing={() => emailRef.current?.focus()}
          editable={!busy}
        />

        {line !== null && (
          <Text
            testID="mobile-login-server-status"
            style={[styles.hint, { color: toneColor(theme, line.tone) }]}
            accessibilityLiveRegion="polite"
          >
            {line.text}
          </Text>
        )}

        <Text style={styles.label}>Email</Text>
        <TextInput
          testID="mobile-login-email"
          ref={emailRef}
          style={styles.input}
          value={email}
          onChangeText={setEmail}
          keyboardType="email-address"
          autoCapitalize="none"
          autoCorrect={false}
          autoComplete="email"
          returnKeyType="next"
          onSubmitEditing={() => passwordRef.current?.focus()}
          editable={!busy}
        />

        <Text style={styles.label}>Password</Text>
        <TextInput
          testID="mobile-login-password"
          ref={passwordRef}
          style={styles.input}
          value={password}
          onChangeText={setPassword}
          secureTextEntry
          autoCapitalize="none"
          autoCorrect={false}
          autoComplete="current-password"
          returnKeyType="go"
          onSubmitEditing={() => void submit()}
          editable={!busy}
        />

        <Pressable
          testID="mobile-login-submit"
          style={[styles.button, (busy || !ready) && styles.buttonBusy]}
          onPress={() => void submit()}
          disabled={busy || !ready}
          accessibilityRole="button"
          accessibilityState={{ disabled: busy || !ready, busy }}
        >
          {busy ? (
            <ActivityIndicator color={theme.colors['accent-fg']} />
          ) : (
            <Text style={styles.buttonText}>Sign in</Text>
          )}
        </Pressable>

        {error !== null && (
          <Text testID="mobile-login-error" style={styles.error} accessibilityRole="alert">
            {error}
          </Text>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function toneColor(theme: Theme, tone: 'muted' | 'ok' | 'danger') {
  return theme.colors[tone];
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    fill: { flex: 1, backgroundColor: theme.colors.bg },
    content: { paddingHorizontal: theme.space[4], flexGrow: 1 },
    title: {
      fontSize: theme.text.lg,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
    },
    subtitle: {
      fontSize: theme.text.base,
      color: theme.colors.muted,
      marginTop: theme.space[1],
      marginBottom: theme.space[5],
    },
    label: {
      fontSize: theme.text.sm,
      fontWeight: theme.weight.medium,
      color: theme.colors.muted,
      marginTop: theme.space[3],
      marginBottom: theme.space[1],
    },
    input: {
      height: theme.size.row,
      paddingHorizontal: theme.space[3],
      fontSize: theme.text.md,
      color: theme.colors.text,
      backgroundColor: theme.colors.surface,
      borderWidth: 1,
      borderColor: theme.colors.border,
      borderRadius: theme.radius.md,
    },
    button: {
      height: theme.size.row,
      marginTop: theme.space[5],
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: theme.colors.accent,
      borderRadius: theme.radius.md,
    },
    hint: { marginTop: theme.space[1], fontSize: theme.text.sm },
    buttonBusy: { opacity: 0.7 },
    buttonText: {
      fontSize: theme.text.md,
      fontWeight: theme.weight.semibold,
      color: theme.colors['accent-fg'],
    },
    error: {
      marginTop: theme.space[3],
      fontSize: theme.text.sm,
      color: theme.colors.danger,
    },
  });
}
