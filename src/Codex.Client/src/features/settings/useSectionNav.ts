import { useEffect, useState, type RefObject } from 'react';
import { useLocation } from 'react-router-dom';
import type { SettingsSection } from '@codex/core/shell/deepLink';
import { navSections } from './sections';

const FLASH_CLASS = 'settings-flash';

function scroller(root: HTMLElement): HTMLElement | Window {
  for (let node = root.parentElement; node; node = node.parentElement) {
    const overflow = getComputedStyle(node).overflowY;
    if (overflow === 'auto' || overflow === 'scroll') return node;
  }
  return window;
}

const navSectionOf = (section: SettingsSection | undefined): string | undefined =>
  section === 'rules' || section === 'letters' ? 'citations' : section;

/**
 * The section whose card top has passed the top of the scrolling area (the last one at the bottom). A section
 * reached by a link or the nav stays current until the reader scrolls on their own, because a short page can't
 * bring every card to the top.
 */
export function useActiveSection(
  rootRef: RefObject<HTMLElement | null>,
  target: SettingsSection | undefined,
): string {
  const { key } = useLocation();
  const [active, setActive] = useState<string>(navSections[0].section);
  const token = `${key}|${target ?? ''}`;
  const [pin, setPin] = useState<{ id: string | undefined; token: string }>({
    id: undefined,
    token: '',
  });
  if (pin.token !== token) setPin({ id: navSectionOf(target), token });
  useEffect(() => {
    const root = rootRef.current;
    if (root === null) return;
    const target = scroller(root);
    const unpin = () => setPin((current) => ({ ...current, id: undefined }));
    const update = () => {
      const top = target instanceof Window ? 0 : target.getBoundingClientRect().top;
      let current: string = navSections[0].section;
      for (const card of root.querySelectorAll<HTMLElement>('[data-section]')) {
        if (card.getBoundingClientRect().top - top < 140) current = card.dataset.section ?? current;
      }
      const atEnd =
        target instanceof Window
          ? window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 4
          : target.scrollTop + target.clientHeight >= target.scrollHeight - 4;
      const scrollable =
        target instanceof Window
          ? document.documentElement.scrollHeight > window.innerHeight
          : target.scrollHeight > target.clientHeight;
      if (atEnd && scrollable) current = navSections[navSections.length - 1].section;
      setActive(current);
    };
    update();
    target.addEventListener('scroll', update, { passive: true });
    target.addEventListener('wheel', unpin, { passive: true });
    target.addEventListener('touchmove', unpin, { passive: true });
    window.addEventListener('keydown', unpin);
    return () => {
      target.removeEventListener('scroll', update);
      target.removeEventListener('wheel', unpin);
      target.removeEventListener('touchmove', unpin);
      window.removeEventListener('keydown', unpin);
    };
  }, [rootRef]);
  return pin.id ?? active;
}

/** `#section` -> scroll to the card (or the anchor inside it) and flash it once. */
export function useSectionTarget(section: SettingsSection | undefined, ready: boolean) {
  const { key } = useLocation();
  useEffect(() => {
    if (section === undefined || !ready) return;
    const element = document.getElementById(section);
    if (element === null) return;
    element.scrollIntoView({ block: 'start' });
    element.classList.remove(FLASH_CLASS);
    void element.offsetWidth;
    element.classList.add(FLASH_CLASS);
    const clear = () => element.classList.remove(FLASH_CLASS);
    element.addEventListener('animationend', clear, { once: true });
    if (section === 'rules') document.getElementById('settings-quote-limit')?.focus();
    return () => element.removeEventListener('animationend', clear);
  }, [section, ready, key]);
}
