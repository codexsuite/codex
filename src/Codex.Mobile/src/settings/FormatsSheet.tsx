import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { PublicationFormat } from '@codex/core/api/settings';
import { formatOptions } from '@codex/core/features/settings/publicationDraft';
import { useStyles } from '~/theme/useStyles';
import type { Theme } from '~/theme';
import { Chip } from '~/ui';
import { FieldSheet } from './FieldSheet';
import { settingsTestIds } from './testIds';

/** The formats permission letters ask about: check chips, as on the desktop. */
export function FormatsSheet({
  selected,
  onSave,
  onClose,
  secondary,
}: {
  selected: PublicationFormat[];
  onSave: (formats: PublicationFormat[]) => Promise<void>;
  onClose: () => void;
  secondary?: { label: string; onPress: () => Promise<void> };
}) {
  const { styles } = useStyles(makeStyles);
  const [picked, setPicked] = useState(selected);
  const toggle = (code: PublicationFormat) =>
    setPicked((now) => (now.includes(code) ? now.filter((c) => c !== code) : [...now, code]));
  return (
    <FieldSheet
      title="Formats"
      onClose={onClose}
      onSave={() => {
        const formats = formatOptions.map((o) => o.code).filter((c) => picked.includes(c));
        const same =
          formats.length === selected.length && formats.every((c) => selected.includes(c));
        return same ? Promise.resolve() : onSave(formats);
      }}
      secondary={secondary}
    >
      <View style={styles.chips}>
        {formatOptions.map((o) => (
          <Chip
            key={o.code}
            label={o.label}
            selected={picked.includes(o.code)}
            onPress={() => toggle(o.code)}
            testID={settingsTestIds.choice(o.code)}
          />
        ))}
      </View>
    </FieldSheet>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[2] },
  });
}
