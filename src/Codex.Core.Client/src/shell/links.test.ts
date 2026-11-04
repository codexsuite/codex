import { describe, expect, it } from 'vitest';
import fixture from '../../../Codex.TestSupport/Fixtures/links/links.json';
import type { SettingsSection } from './deepLink';
import { layoutLink, planLink, referencesLink, seriesLink, settingsLink, writeLink } from './links';

// The same cases the C# CodexLinksTests runs through CodexLinks: an MCP open_url is exactly the URL the UI builds (D8).
type Args = Partial<
  Record<'scene' | 'chapter' | 'kb' | 'entry' | 'ref' | 'source' | 'section', string>
>;

interface LinkCase {
  builder: string;
  id: string;
  args: Args;
  url: string;
}

function build({ builder, id, args }: LinkCase): string {
  switch (builder) {
    case 'plan':
      return planLink(id, args);
    case 'write':
      return writeLink(id, args);
    case 'references':
      return referencesLink(id, { source: args.source });
    case 'settings':
      return settingsLink(id, args.section as SettingsSection | undefined);
    case 'series':
      return seriesLink(id);
    case 'layout':
      return layoutLink(id, args);
    default:
      throw new Error(`Unknown builder in links.json: ${builder}`);
  }
}

const cases: LinkCase[] = fixture.cases;

describe('link builders against the shared links.json', () => {
  it.each(cases.map((c) => [`${c.builder} ${Object.keys(c.args).join(',')}`, c] as const))(
    '%s',
    (_name, link) => {
      expect(build(link)).toBe(link.url);
    },
  );

  it('covers every builder the server mirrors', () => {
    expect(new Set(cases.map((c) => c.builder))).toEqual(
      new Set(['plan', 'write', 'references', 'settings', 'series', 'layout']),
    );
  });
});
