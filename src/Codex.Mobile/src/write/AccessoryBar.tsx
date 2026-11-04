import type { EditorCommand } from '@codex/core/editing/editorBridge';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import type { EditorFormat } from './useEditorSession';
import { writeTestIds } from './testIds';

interface Key {
  command: EditorCommand;
  glyph: string;
  label: string;
  /** The mark this key shows as active while the selection is inside it. */
  active?: 'bold' | 'italic' | 'quote';
  emphasis?: 'bold' | 'italic';
  wide?: boolean;
}

const keys: readonly Key[] = [
  { command: 'slash', glyph: '/', label: 'Commands' },
  { command: 'mention', glyph: '@', label: 'Mention entry' },
  { command: 'beat', glyph: 'Beat', label: 'Insert beat', wide: true },
  { command: 'newScene', glyph: '◇', label: 'Scene break' },
  { command: 'bold', glyph: 'B', label: 'Bold', active: 'bold', emphasis: 'bold' },
  { command: 'italic', glyph: 'I', label: 'Italic', active: 'italic', emphasis: 'italic' },
  { command: 'quote', glyph: '❝', label: 'Block quote', active: 'quote' },
];

export interface AccessoryBarProps {
  format: EditorFormat;
  onCommand: (command: EditorCommand) => void;
}

/** The bar above the keyboard while the page has focus: `/` `@` Beat ◇ B I ❝ and ⌄ (close the keyboard). */
export function AccessoryBar({ format, onCommand }: AccessoryBarProps) {
  const { styles } = useStyles(makeStyles);
  return (
    <View testID={writeTestIds.accessoryBar} style={styles.bar}>
      {keys.map((key) => {
        const on = key.active !== undefined && format[key.active];
        return (
          <Pressable
            key={key.command}
            testID={writeTestIds.accessory(key.command)}
            onPress={() => onCommand(key.command)}
            accessibilityRole="button"
            accessibilityLabel={key.label}
            accessibilityState={{ selected: on }}
            style={[styles.key, key.wide && styles.wide, on && styles.on]}
          >
            <Text
              style={[
                styles.glyph,
                key.emphasis === 'bold' && styles.bold,
                key.emphasis === 'italic' && styles.italic,
              ]}
            >
              {key.glyph}
            </Text>
          </Pressable>
        );
      })}
      <View style={styles.spacer} />
      <Pressable
        testID={writeTestIds.accessory('blur')}
        onPress={() => onCommand('blur')}
        accessibilityRole="button"
        accessibilityLabel="Done"
        style={styles.key}
      >
        <Text style={styles.glyph}>{'⌄'}</Text>
      </Pressable>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    bar: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[1],
      paddingHorizontal: theme.space[2],
      height: theme.size.bar,
      backgroundColor: theme.colors['surface-2'],
      borderTopWidth: 1,
      borderTopColor: theme.colors.border,
    },
    key: {
      minWidth: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radius.sm,
    },
    wide: { paddingHorizontal: theme.space[3] },
    on: { backgroundColor: theme.colors['accent-soft'] },
    glyph: { fontSize: theme.text.md, color: theme.colors.text },
    bold: { fontWeight: theme.weight.semibold },
    italic: { fontStyle: 'italic' },
    spacer: { flex: 1 },
  });
}
