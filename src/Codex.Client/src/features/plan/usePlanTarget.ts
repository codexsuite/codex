import { useEffect, useRef } from 'react';
import type { Outline } from '@codex/core/api/structure';
import { liveChapters } from '@codex/core/features/plan/format';
import type { PlanView } from './planPrefs';
import { planTestIds } from './testIds';

interface Target {
  actId: string;
  actIndex: number;
  elementId: string;
  testId: string;
}

// `#scene=5.2` / `#chapter=5` -> the act and element they name, or null when the book has no such thing.
function findTarget(outline: Outline, scene?: string, chapter?: string): Target | null {
  for (const [actIndex, act] of outline.acts.entries()) {
    for (const c of liveChapters(act)) {
      if (chapter !== undefined && scene === undefined && String(c.number) === chapter) {
        return {
          actId: act.id,
          actIndex,
          elementId: `chapter-${c.id}`,
          testId: planTestIds.chapter(c.id),
        };
      }
      const s = c.scenes.find((x) => x.number === scene);
      if (scene !== undefined && s) {
        return {
          actId: act.id,
          actIndex,
          elementId: `scene-${s.id}`,
          testId: planTestIds.scene(s.id),
        };
      }
    }
  }
  return null;
}

interface PlanTargetArgs {
  outline: Outline | undefined;
  scene?: string;
  chapter?: string;
  view: PlanView;
  setView: (v: PlanView) => void;
  isCollapsed: (actId: string, actIndex: number) => boolean;
  setCollapsed: (actId: string, collapsed: boolean) => void;
}

// Brings the deep-link target on screen: switches to the board, expands its act, scrolls to it and flashes it
// (the CSS class goes away when the animation ends). A target that matches nothing is ignored.
export function usePlanTarget({
  outline,
  scene,
  chapter,
  view,
  setView,
  isCollapsed,
  setCollapsed,
}: PlanTargetArgs) {
  const done = useRef<string | null>(null);
  const key =
    scene !== undefined ? `scene:${scene}` : chapter !== undefined ? `chapter:${chapter}` : null;

  useEffect(() => {
    if (key === null) {
      done.current = null;
      return;
    }
    if (outline === undefined || done.current === key) return;
    const target = findTarget(outline, scene, chapter);
    if (target === null) {
      done.current = key;
      return;
    }
    if (view !== 'board') {
      setView('board');
      return;
    }
    if (isCollapsed(target.actId, target.actIndex)) {
      setCollapsed(target.actId, false);
      return;
    }
    const element = document.getElementById(target.elementId);
    if (element === null) return;
    done.current = key;
    element.scrollIntoView({ block: 'center', inline: 'nearest' });
    element.classList.add('plan-flash');
    element.addEventListener('animationend', () => element.classList.remove('plan-flash'), {
      once: true,
    });
  }, [key, outline, scene, chapter, view, setView, isCollapsed, setCollapsed]);
}
