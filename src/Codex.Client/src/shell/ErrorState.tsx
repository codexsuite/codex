import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { libraryLink } from '@codex/core/shell/links';
import { shellTestIds } from './shellTestIds';

interface ErrorStateProps {
  testId: string;
  title: string;
  detail?: string;
  /** Replaces the default "Back to Library" link. */
  action?: ReactNode;
}

// A visible, recoverable error: used for bad links and unknown books instead of a blank screen or a crash.
export function ErrorState({ testId, title, detail, action }: ErrorStateProps) {
  return (
    <div
      role="alert"
      data-testid={testId}
      className="mx-auto flex max-w-md flex-col items-start gap-2 p-8"
    >
      <h2 className="m-0 text-md font-semibold text-text">{title}</h2>
      {detail && <p className="m-0 text-base text-muted">{detail}</p>}
      {action ?? (
        <Link
          to={libraryLink()}
          data-testid={shellTestIds.errorBackLink}
          className="text-sm font-medium text-accent no-underline hover:underline"
        >
          Back to Library
        </Link>
      )}
    </div>
  );
}
