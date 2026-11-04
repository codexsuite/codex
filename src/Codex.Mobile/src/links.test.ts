import { describe, expect, it } from 'vitest';
import {
  accountLink,
  bookLink,
  bookSwitcherLink,
  bookTabs,
  chapterJumpLink,
  entryLink,
  knowledgeLink,
  libraryLink,
  parseRecordTarget,
  planLink,
  recordLink,
  recordTargetParam,
  sceneSheetLink,
  settingsLink,
  speechModelsLink,
  styleGuideLink,
  tabLink,
  writeLink,
} from './links';

describe('links', () => {
  it('root screens are plain paths', () => {
    expect([libraryLink(), accountLink(), speechModelsLink()]).toEqual([
      '/',
      '/account',
      '/speech-models',
    ]);
  });

  it('bookLink_ShouldOpenPlan', () => {
    expect(bookLink('b1')).toEqual(planLink('b1'));
  });

  it('tabLink_ForEachTab_ShouldMatchItsBuilder', () => {
    const builders = {
      plan: planLink,
      write: writeLink,
      knowledge: knowledgeLink,
      settings: settingsLink,
    };
    for (const tab of bookTabs) expect(tabLink('b1', tab)).toEqual(builders[tab]('b1'));
  });

  it('writeLink_WithAScene_ShouldCarryItAsAParam', () => {
    expect(writeLink('b1', { sceneId: 's1' })).toEqual({
      pathname: '/book/[bookId]/write',
      params: { bookId: 'b1', sceneId: 's1' },
    });
    expect(writeLink('b1', { chapterId: 'c1' })).toEqual({
      pathname: '/book/[bookId]/write',
      params: { bookId: 'b1', chapterId: 'c1' },
    });
    expect(writeLink('b1')).toEqual({ pathname: '/book/[bookId]/write', params: { bookId: 'b1' } });
  });

  it('chapterJumpLink_ShouldCarryTheCurrentChapterWhenGiven', () => {
    expect(chapterJumpLink('b1')).toEqual({
      pathname: '/book/[bookId]/chapters',
      params: { bookId: 'b1' },
    });
    expect(chapterJumpLink('b1', 'c1')).toEqual({
      pathname: '/book/[bookId]/chapters',
      params: { bookId: 'b1', chapterId: 'c1' },
    });
  });

  it('sceneSheetLink_ShouldNotLiveUnderATab', () => {
    expect(sceneSheetLink('b1', 's1')).toEqual({
      pathname: '/book/[bookId]/scene/[sceneId]',
      params: { bookId: 'b1', sceneId: 's1' },
    });
  });

  it('pushedScreens_ShouldCarryTheirIds', () => {
    expect(entryLink('b1', 'e1')).toEqual({
      pathname: '/book/[bookId]/knowledge/[entryId]',
      params: { bookId: 'b1', entryId: 'e1' },
    });
    expect(styleGuideLink('b1')).toEqual({
      pathname: '/book/[bookId]/settings/style-guide',
      params: { bookId: 'b1' },
    });
    expect(bookSwitcherLink('b1')).toEqual({
      pathname: '/book-switcher',
      params: { bookId: 'b1' },
    });
  });

  describe('record target', () => {
    it('recordLink_ShouldCarryTheBookAndTheTarget', () => {
      expect(recordLink('b1', { kind: 'scene', id: 's1' })).toEqual({
        pathname: '/record',
        params: { bookId: 'b1', target: 'scene:s1' },
      });
    });

    it.each([
      [{ kind: 'scene', id: 's1' } as const],
      [{ kind: 'entry', id: 'e-1' } as const],
      [{ kind: 'entry', id: 'a:b' } as const],
    ])('parseRecordTarget_OfRecordTargetParam_ShouldRoundTrip', (target) => {
      expect(parseRecordTarget(recordTargetParam(target))).toEqual(target);
    });

    it.each([undefined, '', 'scene', 'scene:', 'book:b1', ':x', 'Scene:x'])(
      'parseRecordTarget_Of%j_ShouldBeNull',
      (param) => {
        expect(parseRecordTarget(param)).toBeNull();
      },
    );
  });
});
