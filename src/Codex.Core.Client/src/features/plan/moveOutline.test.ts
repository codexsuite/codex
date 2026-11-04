import { describe, expect, test } from 'vitest';
import outline from '../../../../Codex.TestSupport/Fixtures/structure/outline.json' with { type: 'json' };
import type { Outline, OutlineChapter } from '../../api/structure';
import { applyMove, stepMove } from './moveOutline';

const base: Outline = outline;
const [preface, first] = base.acts[0].chapters;
const [opening, second] = first.scenes;

const sceneIds = (o: Outline, chapter: OutlineChapter) =>
  o.acts[0].chapters.find((c) => c.id === chapter.id)?.scenes.map((s) => [s.id, s.number]);

describe('applyMove', () => {
  test('Scene_WhenMovedAfterItsSibling_ShouldSwapAndRenumber', () => {
    const moved = applyMove(base, {
      kind: 'scene',
      id: opening.id,
      chapterId: first.id,
      afterId: second.id,
    });

    expect(sceneIds(moved, first)).toEqual([
      [second.id, '1.1'],
      [opening.id, '1.2'],
    ]);
  });

  test('Scene_WhenMovedToAnUnnumberedChapter_ShouldTakeAPlainNumber', () => {
    const moved = applyMove(base, {
      kind: 'scene',
      id: opening.id,
      chapterId: preface.id,
      afterId: null,
    });

    expect(sceneIds(moved, preface)).toEqual([[opening.id, '1']]);
    expect(sceneIds(moved, first)).toEqual([[second.id, '1.1']]);
  });

  test('Chapter_WhenMovedFirst_ShouldKeepTheUnnumberedOneUnnumbered', () => {
    const moved = applyMove(base, {
      kind: 'chapter',
      id: first.id,
      actId: base.acts[0].id,
      afterId: null,
    });

    expect(moved.acts[0].chapters.map((c) => [c.id, c.number])).toEqual([
      [first.id, 1],
      [preface.id, null],
    ]);
  });

  test('Move_WhenItemUnknown_ShouldLeaveTheOutlineAlone', () => {
    expect(applyMove(base, { kind: 'scene', id: 'nope', chapterId: first.id, afterId: null })).toBe(
      base,
    );
  });
});

describe('stepMove', () => {
  test('Scene_WhenAltDown_ShouldMoveAfterTheNextScene', () => {
    expect(stepMove(base, 'scene', opening.id, 'ArrowDown')).toEqual({
      kind: 'scene',
      id: opening.id,
      chapterId: first.id,
      afterId: second.id,
    });
  });

  test('Scene_WhenAltUpOnTheFirstOfTheBook_ShouldDoNothing', () => {
    const atStart = applyMove(base, {
      kind: 'scene',
      id: opening.id,
      chapterId: preface.id,
      afterId: null,
    });
    expect(stepMove(atStart, 'scene', opening.id, 'ArrowUp')).toBeNull();
  });

  test('Scene_WhenAltUpOnTheFirstOfAChapter_ShouldCrossIntoTheEndOfThePreviousChapter', () => {
    expect(stepMove(base, 'scene', opening.id, 'ArrowUp')).toEqual({
      kind: 'scene',
      id: opening.id,
      chapterId: preface.id,
      afterId: null,
    });
  });

  test('Scene_WhenAltDownOnTheLastOfAChapter_ShouldCrossIntoTheStartOfTheNextChapter', () => {
    const twoChapters = applyMove(base, {
      kind: 'scene',
      id: opening.id,
      chapterId: preface.id,
      afterId: null,
    });
    expect(stepMove(twoChapters, 'scene', opening.id, 'ArrowDown')).toEqual({
      kind: 'scene',
      id: opening.id,
      chapterId: first.id,
      afterId: null,
    });
  });

  test('Scene_WhenAltLeft_ShouldGoToTheEndOfThePreviousChapter', () => {
    expect(stepMove(base, 'scene', opening.id, 'ArrowLeft')).toEqual({
      kind: 'scene',
      id: opening.id,
      chapterId: preface.id,
      afterId: null,
    });
  });

  test('Chapter_WhenAltLeft_ShouldMoveBeforeThePreviousOne', () => {
    expect(stepMove(base, 'chapter', first.id, 'ArrowLeft')).toEqual({
      kind: 'chapter',
      id: first.id,
      actId: base.acts[0].id,
      afterId: null,
    });
  });

  test('Chapter_WhenAltUpInTheFirstAct_ShouldDoNothing', () => {
    expect(stepMove(base, 'chapter', first.id, 'ArrowUp')).toBeNull();
  });

  test('Chapter_WhenAltDownPastTheActEdge_ShouldGoToTheStartOfTheNextAct', () => {
    const second = { ...base.acts[0], id: 'act-2', chapters: [] };
    const two: Outline = { ...base, acts: [base.acts[0], second] };
    expect(stepMove(two, 'chapter', first.id, 'ArrowDown')).toEqual({
      kind: 'chapter',
      id: first.id,
      actId: 'act-2',
      afterId: null,
    });
    const back = applyMove(two, { kind: 'chapter', id: first.id, actId: 'act-2', afterId: null });
    expect(stepMove(back, 'chapter', first.id, 'ArrowUp')).toEqual({
      kind: 'chapter',
      id: first.id,
      actId: base.acts[0].id,
      afterId: preface.id,
    });
  });
});
