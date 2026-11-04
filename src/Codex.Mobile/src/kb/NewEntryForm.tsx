import { ApiRequestError } from '@codex/core/api/client';
import { kbKey } from '@codex/core/api/kb';
import { createKbEntry } from '@codex/core/api/kbEntries';
import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { entryLink } from '~/links';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Button, Chip, TextField } from '~/ui';
import { kbTestIds } from './testIds';

interface Category {
  id: string;
  name: string;
}

/** A new entry: a name and its category. Creating opens the entry. A name another entry has asks to be confirmed. */
export function NewEntryForm({
  bookId,
  categories,
  categoryId,
  onCancel,
}: {
  bookId: string;
  categories: Category[];
  categoryId: string | null;
  onCancel: () => void;
}) {
  const { styles } = useStyles(makeStyles);
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [chosen, setChosen] = useState(categoryId ?? categories[0]?.id ?? null);
  const [error, setError] = useState<string | null>(null);
  const [duplicate, setDuplicate] = useState(false);
  const [busy, setBusy] = useState(false);

  async function create() {
    const trimmed = name.trim();
    if (trimmed === '') return setError('An entry needs a name.');
    if (chosen === null) return;
    setBusy(true);
    try {
      const entry = await createKbEntry(bookId, {
        categoryId: chosen,
        name: trimmed,
        ...(duplicate ? { allowDuplicate: true } : {}),
      });
      await queryClient.invalidateQueries({ queryKey: kbKey(bookId) });
      onCancel();
      router.push(entryLink(bookId, entry.id));
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'The entry could not be created.');
      setDuplicate(e instanceof ApiRequestError && e.status === 409);
    } finally {
      setBusy(false);
    }
  }

  return (
    <View testID={kbTestIds.newForm} style={styles.form}>
      <TextField
        testID={kbTestIds.newName}
        label="New entry"
        placeholder="Name"
        value={name}
        onChangeText={(text) => {
          setName(text);
          setError(null);
          setDuplicate(false);
        }}
        onSubmitEditing={() => void create()}
        returnKeyType="done"
        autoCapitalize="words"
      />
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.chips}
      >
        {categories.map((c) => (
          <Chip
            key={c.id}
            testID={kbTestIds.newCategory(c.id)}
            label={c.name}
            selected={c.id === chosen}
            onPress={() => setChosen(c.id)}
          />
        ))}
      </ScrollView>
      {categories.length === 0 && (
        <Text style={styles.error}>This book has no categories yet. Add one on desktop first.</Text>
      )}
      {error !== null && (
        <Text testID={kbTestIds.newError} accessibilityRole="alert" style={styles.error}>
          {error}
        </Text>
      )}
      <View style={styles.buttons}>
        <Button testID={kbTestIds.newCancel} label="Cancel" onPress={onCancel} grow />
        <Button
          testID={kbTestIds.newCreate}
          label={duplicate ? 'Create anyway' : 'Create'}
          variant="primary"
          busy={busy}
          disabled={chosen === null}
          onPress={() => void create()}
          grow
        />
      </View>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    form: {
      gap: theme.space[2],
      margin: theme.space[4],
      marginBottom: 0,
      padding: theme.space[3],
      borderRadius: theme.radius.lg,
      backgroundColor: theme.colors.surface,
      borderWidth: StyleSheet.hairlineWidth,
      borderColor: theme.colors.border,
    },
    chips: { gap: theme.space[2] },
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
    buttons: { flexDirection: 'row', gap: theme.space[2] },
  });
}
