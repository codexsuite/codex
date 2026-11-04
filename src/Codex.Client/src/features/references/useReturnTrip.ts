import { useNavigate, useParams } from 'react-router-dom';
import { useDeepLink } from '../../shell/useDeepLink';
import { returnLink, returnTarget } from '../write/citeReturn';
import type { ReturnMode } from './add/AddSourceForm';

/** "Write · Chapter 7 · Scene 1" for a return to Write at scene 7.1. */
function returnLabel(returnTo: string): string {
  const scene = returnTarget(returnTo)?.scene;
  if (scene === undefined) return 'Write';
  const [chapter, number] = scene.split('.');
  return number === undefined
    ? `Write · Scene ${chapter}`
    : `Write · Chapter ${chapter} · Scene ${number}`;
}

/**
 * The detour from Write's New source… (`?return=<path>&sel=<selection>`), or null when References was opened plainly
 * or `return` is not Write in this book (never followed: only links built here are). Done goes back with the source to
 * cite at the selection; Cancel goes back to the scene.
 */
export function useReturnTrip(): ReturnMode | null {
  const { bookId = '' } = useParams();
  const deepLink = useDeepLink();
  const navigate = useNavigate();
  const link = deepLink.ok ? deepLink.link : null;
  const returnTo = link?.returnTo;
  const back = returnTo === undefined ? null : returnLink(bookId, returnTo);
  if (returnTo === undefined || back === null) return null;
  return {
    label: returnLabel(returnTo),
    onDone: (sourceId, title) =>
      void navigate(
        returnLink(bookId, returnTo, { sel: link?.sel, cite: sourceId, title }) ?? back,
      ),
    onCancel: () => void navigate(back),
  };
}
