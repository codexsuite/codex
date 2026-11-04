import { router } from 'expo-router';
import { useEffect, useRef, type ReactNode } from 'react';
import { Animated, KeyboardAvoidingView, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useStyles } from '../theme/useStyles';
import type { Theme } from '../theme';
import { Button, type ButtonProps } from './Button';
import { part, shellTestIds } from './testIds';

export interface SheetDockButton extends Pick<
  ButtonProps,
  'label' | 'onPress' | 'disabled' | 'busy' | 'variant'
> {
  testID?: string;
}

export interface SheetProps {
  title: string;
  children: ReactNode;
  /** Closing: the dimmed area, the ×. Defaults to going back one screen (the sheet is its own route). */
  onClose?: () => void;
  /**
   * The dock of two buttons at the foot, the primary on the right. `secondary` may be left out for a single button.
   * A sheet with no actions has no dock.
   */
  dock?: { primary: SheetDockButton; secondary?: SheetDockButton };
  testID?: string;
}

function goBack() {
  if (router.canGoBack()) router.back();
  else router.replace('/');
}

/**
 * A bottom sheet over a dimmed screen: grab handle, title, ×, the content, and an optional dock. Render it as the whole
 * body of a route presented as a transparent modal (see the route layouts): the dimmed area is this component.
 */
export function Sheet({
  title,
  children,
  onClose = goBack,
  dock,
  testID = shellTestIds.sheet,
}: SheetProps) {
  const { styles } = useStyles(makeStyles);
  const insets = useSafeAreaInsets();
  const slide = useRef(new Animated.Value(40)).current;
  useEffect(() => {
    Animated.timing(slide, { toValue: 0, duration: 180, useNativeDriver: true }).start();
  }, [slide]);

  return (
    <View testID={testID} style={styles.root}>
      <Pressable
        testID={part(testID, 'backdrop')}
        style={styles.backdrop}
        onPress={onClose}
        accessibilityLabel="Close"
        accessibilityRole="button"
      />
      <KeyboardAvoidingView behavior="padding" style={styles.avoid}>
        <Animated.View
          style={[
            styles.panel,
            { paddingBottom: insets.bottom, transform: [{ translateY: slide }] },
          ]}
        >
          <View style={styles.handle} />
          <View style={styles.head}>
            <Text
              testID={part(testID, 'title')}
              accessibilityRole="header"
              style={styles.title}
              numberOfLines={1}
            >
              {title}
            </Text>
            <Pressable
              testID={part(testID, 'close')}
              onPress={onClose}
              hitSlop={8}
              accessibilityLabel="Close"
              accessibilityRole="button"
              style={styles.close}
            >
              <Text style={styles.closeMark}>{'×'}</Text>
            </Pressable>
          </View>
          <View style={styles.content}>{children}</View>
          {dock !== undefined && (
            <View testID={part(testID, 'dock')} style={styles.dock}>
              {dock.secondary !== undefined && (
                <Button
                  grow
                  variant="secondary"
                  {...dock.secondary}
                  testID={dock.secondary.testID ?? part(testID, 'secondary')}
                />
              )}
              <Button
                grow
                variant="primary"
                {...dock.primary}
                testID={dock.primary.testID ?? part(testID, 'primary')}
              />
            </View>
          )}
        </Animated.View>
      </KeyboardAvoidingView>
    </View>
  );
}

function makeStyles(theme: Theme) {
  return StyleSheet.create({
    root: { flex: 1, justifyContent: 'flex-end' },
    backdrop: { ...StyleSheet.absoluteFill, backgroundColor: 'rgba(8, 12, 20, 0.45)' },
    avoid: { maxHeight: '92%' },
    panel: {
      backgroundColor: theme.colors.surface,
      borderTopLeftRadius: theme.radius.lg * 2,
      borderTopRightRadius: theme.radius.lg * 2,
      borderWidth: 1,
      borderBottomWidth: 0,
      borderColor: theme.colors.border,
    },
    handle: {
      alignSelf: 'center',
      width: 36,
      height: 5,
      marginTop: theme.space[2],
      borderRadius: 3,
      backgroundColor: theme.colors.border,
    },
    head: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.space[2],
      paddingHorizontal: theme.space[4],
      paddingTop: theme.space[2],
      paddingBottom: theme.space[2],
    },
    title: {
      flex: 1,
      fontSize: theme.text.md,
      fontWeight: theme.weight.semibold,
      color: theme.colors.text,
    },
    close: {
      width: theme.size.control,
      height: theme.size.control,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radius.sm,
    },
    closeMark: { fontSize: theme.text.lg, color: theme.colors.muted },
    content: { flexShrink: 1 },
    dock: {
      flexDirection: 'row',
      gap: theme.space[2],
      padding: theme.space[4],
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
  });
}
