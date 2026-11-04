import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { writeTestIds } from './testIds';

export interface ChapterPillProps {
  /** `1 · An Empty Seat`, plus `· Scene 2` while editing. */
  label: string;
  /** Editing: the pill gives up ‹ › and takes undo and redo. */
  editing: boolean;
  canUndo: boolean;
  canRedo: boolean;
  onPrevious: (() => void) | null;
  onNext: (() => void) | null;
  onJump: () => void;
  onUndo: () => void;
  onRedo: () => void;
}

// TODO: draw real icons (chevrons, undo, redo, down) instead of these glyphs.
/** The strip under the book bar: ‹ chapter pill › (reading), or chapter pill, undo and redo (editing). */
export function ChapterPill({
  label,
  editing,
  canUndo,
  canRedo,
  onPrevious,
  onNext,
  onJump,
  onUndo,
  onRedo,
}: ChapterPillProps) {
  const { styles } = useStyles(makeStyles);
  return (
    <View style={styles.strip}>
      {!editing && (
        <IconButton
          testID={writeTestIds.pillPrevious}
          glyph={'‹'}
          label="Previous chapter"
          onPress={onPrevious}
        />
      )}
      <Pressable
        testID={writeTestIds.pill}
        onPress={onJump}
        accessibilityRole="button"
        accessibilityLabel="Jump to chapter"
        style={styles.pill}
      >
        <Text testID={writeTestIds.pillLabel} style={styles.pillLabel} numberOfLines={1}>
          {label}
        </Text>
        <Text style={styles.down}>{'⌄'}</Text>
      </Pressable>
      {editing ? (
        <>
          <IconButton
            testID={writeTestIds.undo}
            glyph={'↶'}
            label="Undo"
            onPress={canUndo ? onUndo : null}
          />
          <IconButton
            testID={writeTestIds.redo}
            glyph={'↷'}
            label="Redo"
            onPress={canRedo ? onRedo : null}
          />
        </>
      ) : (
        <IconButton
          testID={writeTestIds.pillNext}
          glyph={'›'}
          label="Next chapter"
          onPress={onNext}
        />
      )}
    </View>
  );
}

function IconButton({
  testID,
  glyph,
  label,
  onPress,
}: {
  testID: string;
  glyph: string;
  label: string;
  onPress: (() => void) | null;
}) {
  const { styles } = useStyles(makeStyles);
  return (
    <Pressable
      testID={testID}
      onPress={onPress ?? undefined}
      disabled={onPress === null}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: onPress === null }}
      style={styles.icon}
    >
      <Text style={[styles.iconGlyph, onPress === null && styles.disabled]}>{glyph}</Text>
    </Pressable>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    strip: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[1],
      paddingHorizontal: theme.space[3],
      paddingVertical: theme.space[2],
      backgroundColor: theme.colors.bg,
      borderBottomWidth: 1,
      borderBottomColor: theme.colors.border,
    },
    pill: {
      flex: 1,
      minWidth: 0,
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: theme.space[2],
      height: theme.size.control,
      paddingHorizontal: theme.space[3],
      borderRadius: theme.size.control / 2,
      backgroundColor: theme.colors['surface-2'],
    },
    pillLabel: {
      flexShrink: 1,
      fontSize: theme.text.sm,
      fontWeight: theme.weight.medium,
      color: theme.colors.text,
    },
    down: { fontSize: theme.text.sm, color: theme.colors.muted },
    icon: {
      width: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
    },
    iconGlyph: { fontSize: theme.text.lg, color: theme.colors.text },
    disabled: { color: theme.colors.muted, opacity: 0.4 },
  });
}
