// How a route is presented when its screen is a `Sheet`: a transparent modal, so the dimmed screen under it shows.
// Use it as the `options` of the route's `Stack.Screen` in a layout.
export const sheetScreenOptions = {
  presentation: 'transparentModal',
  animation: 'fade',
  contentStyle: { backgroundColor: 'transparent' },
} as const;
