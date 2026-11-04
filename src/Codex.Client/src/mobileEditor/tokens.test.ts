import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// The page ships its own copy of the tokens it uses (it does not load the SPA's Tailwind stylesheet). design/tokens.css
// is the source of truth: every value here must match it, block for block.

type Block = Map<string, string>;

function blocks(css: string): Map<string, Block> {
  const result = new Map<string, Block>();
  const withoutComments = css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/@import url\([^)]*\);/g, '');
  for (const [, selectorRaw, body] of withoutComments.matchAll(/([^{}@]+)\{([^{}]*)\}/g)) {
    const selector = selectorRaw!.trim().replaceAll('"', "'");
    const block = result.get(selector) ?? new Map<string, string>();
    for (const [, name, value] of body!.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
      block.set(
        name!,
        value!
          .trim()
          .replaceAll('"', "'")
          .replace(/\s+/g, ' ')
          .replace(/(\D)\.(\d)/g, '$10.$2'),
      );
    }
    result.set(selector, block);
  }
  return result;
}

const source = blocks(
  readFileSync(resolve(import.meta.dirname, '../../../../design/tokens.css'), 'utf8'),
);
const mine = blocks(readFileSync(resolve(import.meta.dirname, 'tokens.css'), 'utf8'));
// The SPA ships Inter as `Inter Variable` from @fontsource-variable/inter; the design file names plain Inter.
const ownedHere = new Set(['--font-sans']);

describe('mobile editor tokens', () => {
  it.each([':root', ":root[data-theme='dark']", ":root[data-density='touch']"])(
    'tokens_When%sIsCompared_ShouldMatchDesignTokensCss',
    (selector) => {
      const ours = mine.get(selector);
      const theirs = source.get(selector);
      expect(ours, `${selector} in tokens.css`).toBeDefined();
      expect(theirs, `${selector} in design/tokens.css`).toBeDefined();
      for (const [name, value] of ours!) {
        if (ownedHere.has(name)) continue;
        expect(`${name}: ${theirs!.get(name)}`).toBe(`${name}: ${value}`);
      }
    },
  );
});

describe('mobile editor stylesheets', () => {
  // Set inline by a component (`style={{ '--cat': … }}`), not a token.
  const setInline = new Set(['--cat']);
  const defined = new Set([...mine.values()].flatMap((block) => [...block.keys()]));

  it.each(['mobileEditor.css', '../prose/prose.css'])(
    'stylesheet_When%sUsesAVariable_ShouldFindItInTokensCss',
    (file) => {
      const css = readFileSync(resolve(import.meta.dirname, file), 'utf8');
      const used = [...css.matchAll(/var\((--[\w-]+)/g)].map((m) => m[1]!);
      const missing = [...new Set(used)].filter(
        (name) => !defined.has(name) && !setInline.has(name),
      );
      expect(missing).toEqual([]);
    },
  );
});
