// Shell elements that have no entry in the S0 registry (src/testIds.ts). Same `shell-` prefix, kept apart so the
// registry file and its C# mirror stay as S0 wrote them. C# mirror: Codex.AcceptanceTests/Pages/Shell/ShellTestIds.cs.
export const shellTestIds = {
  themeToggle: 'shell-theme-toggle',
  paletteButton: 'shell-palette-button',
  bookTitle: 'shell-book-title',
  bookCover: 'shell-book-cover',
  kbEmpty: 'shell-kb-empty',
  notFound: 'shell-not-found',
  errorBackLink: 'shell-error-back-link',
  errorRecoverLink: 'shell-error-recover-link',
  referencesBadge: 'shell-references-badge',
  layoutBadge: 'shell-layout-badge',
  sidebarToggle: 'shell-sidebar-toggle',
  avatar: 'shell-avatar',
} as const;
