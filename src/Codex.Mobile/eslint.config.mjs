import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';

// The mobile app (#35). Shared logic lives in @codex/core; this package holds the React Native views, the platform
// adapter and the session store. The SPA's browser and view packages never belong here.
const webOnly = {
  group: [
    'react-dom',
    'react-dom/*',
    'react-router',
    'react-router-dom',
    '@tiptap/*',
    '@dnd-kit/*',
  ],
  message: 'Web-only: React Native views and expo-router here.',
};
const otherPackages = {
  regex: '^\\.\\./Codex\\.',
  message: 'Import other packages by name (@codex/core/...).',
};

export default tseslint.config(
  { ignores: ['.expo/**', 'expo-env.d.ts', 'metro.config.js'] },
  eslint.configs.recommended,
  tseslint.configs.recommended,
  {
    files: ['**/*.ts', '**/*.tsx'],
    rules: { 'no-restricted-imports': ['error', { patterns: [webOnly, otherPackages] }] },
  },
  // Test support (fakes, render helpers, mocks) is for *.test.ts and *.spec.{ts,tsx} files only; the shipped bundle never
  // imports it.
  {
    files: ['app/**/*.{ts,tsx}', 'src/**/*.{ts,tsx}'],
    ignores: [
      'src/**/*.test.ts',
      'src/**/*.spec.ts',
      'src/**/*.spec.tsx',
      'src/**/test-support/**',
    ],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            webOnly,
            otherPackages,
            {
              regex: 'test-support',
              message: 'Test support is for *.test.ts and *.spec.{ts,tsx} files only.',
            },
          ],
        },
      ],
    },
  },
);
