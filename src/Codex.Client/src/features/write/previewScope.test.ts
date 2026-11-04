import { describe, expect, it } from 'vitest';
import manuscript from '../../../../Codex.TestSupport/Fixtures/manuscript/manuscript.json' with { type: 'json' };
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import { buildManuscriptView } from '@codex/core/features/write/manuscriptView';
import { previewTarget } from './previewScope';
import { scopeActs, wholeBook } from '@codex/core/features/write/scope';

const acts = buildManuscriptView(outline, manuscript);
const [preface, first] = acts[0].chapters;
const [scene1, scene2] = first.scenes;

describe('previewTarget', () => {
  it('PreviewTarget_WhenScopeIsAScene_ShouldPreviewThatScene', () => {
    const scope = { kind: 'scene', id: scene2.scene.id } as const;
    const target = previewTarget(scope, scopeActs(acts, scope), first.chapter.id);

    expect(target?.request).toEqual({ scope: 'scene', scopeId: scene2.scene.id });
    expect(target?.anchor).toEqual({ scene: scene2.scene.number });
    expect(target?.versions).toBe(`${scene2.scene.id}@${scene2.version}`);
  });

  it('PreviewTarget_WhenScopeIsAChapter_ShouldPreviewItWithEveryScenesVersion', () => {
    const scope = { kind: 'chapter', id: first.chapter.id } as const;
    const target = previewTarget(scope, scopeActs(acts, scope), null);

    expect(target?.request).toEqual({ scope: 'chapter', scopeId: first.chapter.id });
    expect(target?.anchor).toEqual({ chapter: String(first.chapter.number) });
    expect(target?.label).toBe(`Chapter ${first.chapter.number}: ${first.chapter.title}`);
    expect(target?.versions).toBe(
      `${scene1.scene.id}@${scene1.version},${scene2.scene.id}@${scene2.version}`,
    );
  });

  it('PreviewTarget_WhenScopeIsTheBook_ShouldPreviewTheChapterBeingRead', () => {
    expect(previewTarget(wholeBook, acts, preface.chapter.id)?.request).toEqual({
      scope: 'chapter',
      scopeId: preface.chapter.id,
    });
    expect(previewTarget(wholeBook, acts, preface.chapter.id)?.anchor).toEqual({});
  });

  it('PreviewTarget_WhenNothingIsBeingRead_ShouldPreviewTheFirstChapterWithScenes', () => {
    expect(previewTarget(wholeBook, acts, null)?.request.scopeId).toBe(first.chapter.id);
  });

  it('PreviewTarget_WhenNothingIsInView_ShouldBeNull', () => {
    expect(previewTarget(wholeBook, [], null)).toBeNull();
  });
});
