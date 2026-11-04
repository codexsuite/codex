import { useEffect, useRef } from 'react';

/** Whatever had focus when the dialog mounted gets it back when the dialog goes away. */
export function useReturnFocus() {
  const opener = useRef(document.activeElement);
  useEffect(() => {
    const element = opener.current;
    return () => {
      if (element instanceof HTMLElement && element.isConnected) element.focus();
    };
  }, []);
}
