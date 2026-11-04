import { ApiRequestError } from '@codex/core/api/client';
import { checkKbAlias } from '@codex/core/api/kbEntries';
import { useRef, useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Button, TextField } from '~/ui';
import { AliasChip } from './AliasChip';
import { acceptableAlias } from './entryUses';
import { kbTestIds } from './testIds';

/**
 * The other names that count as this entry: chips with ×, and `+ Alias`. Every change saves at once. A name another
 * entry answers to is refused by the server (409); its message shows with Add anyway. A short or common alias is saved
 * with a warning of how many words it matches (`checkKbAlias`), as on desktop.
 */
export function AliasList({
  entryId,
  name,
  aliases,
  readOnly,
  onChange,
}: {
  entryId: string;
  name: string;
  aliases: string[];
  /** A reader sees the aliases but cannot add or remove. */
  readOnly: boolean;
  /** Saves the whole alias set; rejects with the server's error. */
  onChange: (aliases: string[], allowDuplicate?: boolean) => Promise<unknown>;
}) {
  const { styles } = useStyles(makeStyles);
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [held, setHeld] = useState<string | null>(null);
  const [warning, setWarning] = useState<string | null>(null);
  // One alias change in flight at a time: a second remove would be built from the aliases before the first lands, and
  // submit followed by blur would add the same alias twice.
  const busy = useRef(false);

  async function save(next: string[], allowDuplicate = false): Promise<boolean> {
    if (busy.current) return false;
    busy.current = true;
    try {
      await onChange(next, allowDuplicate);
      setError(null);
      setHeld(null);
      return true;
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'The aliases could not be saved.');
      return false;
    } finally {
      busy.current = false;
    }
  }

  function close() {
    setDraft('');
    setAdding(false);
  }

  async function add() {
    const alias = acceptableAlias(draft, aliases, name);
    if (alias === null) return close();
    if (busy.current) return;
    busy.current = true;
    setWarning(null);
    try {
      await onChange([...aliases, alias], false);
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'The aliases could not be saved.');
      setHeld(e instanceof ApiRequestError && e.status === 409 ? alias : null);
      return;
    } finally {
      busy.current = false;
    }
    setError(null);
    close();
    // The count is advice: when it cannot be made, the alias stays saved without a warning.
    const check = await checkKbAlias(entryId, alias).catch(() => null);
    if (check?.warn) {
      setWarning(
        `Matches ${check.matches} other ${check.matches === 1 ? 'word' : 'words'} in the book. Underlines every "${alias}".`,
      );
    }
  }

  async function addAnyway() {
    if (held !== null && (await save([...aliases, held], true))) close();
  }

  return (
    <View style={styles.box}>
      <View style={styles.chips}>
        {aliases.map((alias) => (
          <AliasChip
            key={alias}
            alias={alias}
            onRemove={readOnly ? undefined : () => void save(aliases.filter((a) => a !== alias))}
          />
        ))}
        {!readOnly && !adding && (
          <Pressable
            testID={kbTestIds.aliasAdd}
            onPress={() => setAdding(true)}
            accessibilityRole="button"
            style={styles.add}
          >
            <Text style={styles.addLabel}>+ Alias</Text>
          </Pressable>
        )}
      </View>
      {adding && (
        <TextField
          testID={kbTestIds.aliasInput}
          label="New alias"
          placeholder="Another name"
          value={draft}
          onChangeText={(text) => {
            setDraft(text);
            setError(null);
            setHeld(null);
          }}
          onSubmitEditing={() => void add()}
          onBlur={() => {
            if (held === null) void add();
          }}
          returnKeyType="done"
          autoCapitalize="words"
        />
      )}
      {error !== null && (
        <Text testID={kbTestIds.aliasError} accessibilityRole="alert" style={styles.error}>
          {error}
        </Text>
      )}
      {held !== null && (
        <Button
          testID={kbTestIds.aliasAddAnyway}
          label="Add anyway"
          onPress={() => void addAnyway()}
        />
      )}
      {warning !== null && (
        <Text testID={kbTestIds.aliasWarning} style={styles.warning}>
          {warning}
        </Text>
      )}
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    box: { gap: theme.space[2], paddingHorizontal: theme.space[4], paddingTop: theme.space[3] },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
    add: { height: 28, justifyContent: 'center', paddingHorizontal: theme.space[2] },
    addLabel: { fontSize: theme.text.sm, color: theme.colors.muted },
    error: { fontSize: theme.text.sm, color: theme.colors.danger },
    warning: { fontSize: theme.text.sm, color: theme.colors.warn },
  });
}
