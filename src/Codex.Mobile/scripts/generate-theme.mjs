// Generates src/theme/tokens.generated.ts from design/tokens.json (#35).
// Plain Node ESM, no dependencies. Run: node scripts/generate-theme.mjs
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const tokensPath = resolve(here, '../../../design/tokens.json');
const outPath = resolve(here, '../src/theme/tokens.generated.ts');

const tokens = JSON.parse(readFileSync(tokensPath, 'utf8'));
const touch = tokens.density.touch;

const key = (k) => (/^[A-Za-z_$][\w$]*$/.test(k) || /^(0|[1-9]\d*)$/.test(k) ? k : `'${k}'`);
const value = (v) => (typeof v === 'string' ? `'${v}'` : String(v));
const object = (entries, indent = '') => {
  const lines = entries.map(([k, v]) => `${indent}  ${key(k)}: ${value(v)},`);
  return `{\n${lines.join('\n')}\n${indent}}`;
};
const plain = (obj) => Object.entries(obj).filter(([k]) => !k.startsWith('$'));

const sizeMap = {
  bar: 'bar-height',
  control: 'control-height',
  row: 'row-height',
  tab: 'tab-height',
  cover: 'cover-width',
};
const sizes = Object.entries(sizeMap).map(([name, token]) => {
  if (typeof touch[token] !== 'number')
    throw new Error(`tokens.json density.touch.${token} missing`);
  return [name, touch[token]];
});

const fontSize = { ...tokens.font.size, ...touch['font-size'] };
const weights = plain(tokens.font.weight).map(([k, v]) => [k, String(v)]);

const out = `// Generated from design/tokens.json by scripts/generate-theme.mjs; do not edit.
// Regenerate with: node scripts/generate-theme.mjs

export const lightColors = ${object(plain(tokens.color.light))} as const;

export const darkColors = ${object(plain(tokens.color.dark))} as const;

/** design/tokens.json font.size with the touch density overrides applied. */
export const fontSize = ${object(plain(fontSize))} as const;

export const fontWeight = ${object(weights)} as const;

/** Touch density sizes (density.touch). */
export const sizes = ${object(sizes)} as const;

export const space = ${object(plain(tokens.space))} as const;

export const radius = ${object(plain(tokens.radius))} as const;

/** Codex category colours, separate from the UI colours. */
export const categoryColors = {
  light: ${object(plain(tokens.category.light), '  ')},
  dark: ${object(plain(tokens.category.dark), '  ')},
} as const;

/** Project status to UI colour name. */
export const statusColors = ${object(plain(tokens.status))} as const;
`;

writeFileSync(outPath, out);
