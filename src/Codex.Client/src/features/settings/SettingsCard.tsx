import type { ReactNode } from 'react';
import { settingsTestIds } from './testIds';

interface SettingsCardProps {
  section: string;
  title: string;
  blurb: string;
  children: ReactNode;
}

// One section card: a deep-link target (`id` is the hash) and the scroll-spy's unit.
export function SettingsCard({ section, title, blurb, children }: SettingsCardProps) {
  return (
    <section
      id={section}
      data-section={section}
      data-testid={settingsTestIds.card(section)}
      className="scroll-mt-14 min-[860px]:scroll-mt-4 rounded-lg border border-border bg-surface"
    >
      <header className="px-4 pt-3">
        <h2 className="m-0 text-md font-semibold text-text">{title}</h2>
        <p className="m-0 mt-0.5 text-xs text-muted">{blurb}</p>
      </header>
      <div className="px-4 pt-2 pb-4">{children}</div>
    </section>
  );
}
