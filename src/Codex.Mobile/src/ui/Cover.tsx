import { coverPalettes, type CoverPalette } from '@codex/core/api/library';
import { platform } from '@codex/core/platform';
import { Image, StyleSheet, Text, View } from 'react-native';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { shellTestIds } from './testIds';

// A book cover is the book's artwork: it looks the same in both themes, so its colours are not UI tokens. The same
// palette names and colours as the web Library's Cover (`cover-1` to `cover-8`, `[from, to, ink]`).
const covers = {
  'cover-1': ['#05070d', '#0d1a2b', '#22d3ee'],
  'cover-2': ['#d9cfc0', '#b9ab97', '#5b4f43'],
  'cover-3': ['#2a1d12', '#5c3b1e', '#f3d7a4'],
  'cover-4': ['#1b2320', '#3d4f45', '#c9e4d5'],
  'cover-5': ['#3b0d1f', '#9d174d', '#fbcfe8'],
  'cover-6': ['#1d1d2b', '#45306b', '#d9c7ff'],
  'cover-7': ['#08263a', '#155e75', '#bae6fd'],
  'cover-8': ['#3a1406', '#a3410f', '#ffe1c2'],
} as const;

export interface CoverProps {
  /** `coverPalette` from the DTO. */
  palette: string;
  title: string;
  /** `coverUrl`: the uploaded image (a path on the server); null or omitted draws the generated cover. */
  imageUrl?: string | null;
  /** Width in points; defaults to the touch list-row cover (30). The height is 1.5 times it. */
  width?: number;
  testID?: string;
}

export function Cover({
  palette,
  title,
  imageUrl = null,
  width,
  testID = shellTestIds.cover,
}: CoverProps) {
  const { theme, styles } = useStyles(makeStyles);
  const w = width ?? theme.size.cover;
  const frame = { width: w, height: w * 1.5 };
  const known = coverPalettes.find((name): name is CoverPalette => name === palette);

  if (known === undefined) {
    // A contract violation stays visible instead of crashing the screen that shows the cover.
    return (
      <View
        testID={testID}
        accessibilityLabel={`Unknown cover palette ${palette}`}
        style={[styles.frame, styles.unknown, frame]}
      >
        <Text style={styles.unknownMark}>?</Text>
      </View>
    );
  }
  if (imageUrl !== null) {
    return (
      <Image
        testID={testID}
        accessibilityLabel={title}
        source={{ uri: `${platform().apiOrigin()}${imageUrl}`, headers: platform().authHeaders() }}
        style={[styles.frame, frame]}
        resizeMode="cover"
      />
    );
  }
  const [from, to, ink] = covers[known];
  return (
    <View
      testID={testID}
      accessibilityLabel={title}
      style={[styles.frame, frame, { backgroundColor: from }]}
    >
      <View style={[styles.lowerHalf, { backgroundColor: to }]} />
      <Text
        numberOfLines={4}
        style={[styles.title, { color: ink, fontSize: Math.max(5, w * 0.1) }]}
      >
        {title}
      </Text>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    frame: { borderRadius: theme.radius.xs, overflow: 'hidden', padding: '8%' },
    // TODO: draw the cover's diagonal gradient (expo-linear-gradient) instead of two flat halves.
    lowerHalf: { position: 'absolute', left: 0, right: 0, bottom: 0, height: '50%' },
    title: { fontWeight: theme.weight.semibold, textTransform: 'uppercase' },
    unknown: {
      borderWidth: 2,
      borderStyle: 'dashed',
      borderColor: theme.colors.danger,
      alignItems: 'center',
      justifyContent: 'center',
    },
    unknownMark: { color: theme.colors.danger, fontWeight: theme.weight.semibold },
  });
}
