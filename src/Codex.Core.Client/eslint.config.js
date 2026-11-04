import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';

// The core is shared by the web SPA and the mobile app (#35): no React, router, editor or drag and drop, and no browser
// globals. What the core needs from its host goes through `src/platform.ts`. tsconfig.json leaves DOM out as well.
const viewPackages = {
  group: [
    'react',
    'react-dom',
    'react-dom/*',
    'react-router',
    'react-router-dom',
    '@tanstack/react-query',
    '@tiptap/*',
    '@dnd-kit/*',
    '@codemirror/*',
    'prosemirror-*',
  ],
  message: 'The core is platform-neutral: views, hooks and the editor stay in the apps.',
};

const browserGlobals = [
  'window',
  'document',
  'localStorage',
  'sessionStorage',
  'navigator',
  'location',
  'self',
  'addEventListener',
  'removeEventListener',
  'dispatchEvent',
].map((name) => ({
  name,
  message: 'Not in React Native: go through platform() (src/platform.ts).',
}));

export default tseslint.config(
  eslint.configs.recommended,
  tseslint.configs.recommended,
  {
    files: ['src/**/*.ts'],
    rules: {
      'no-restricted-imports': ['error', { patterns: [viewPackages] }],
      'no-restricted-globals': ['error', ...browserGlobals],
    },
  },
  // Test support (fetch stubs) is for tests only.
  {
    files: ['src/**/*.ts'],
    ignores: ['src/**/*.test.ts', 'src/test-support/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            viewPackages,
            { regex: 'test-support', message: 'Test support is for *.test.ts files only.' },
          ],
        },
      ],
    },
  },
);
