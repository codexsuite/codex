import { useEffect, useState } from 'react';
import { useSpeechEngine } from './SpeechEngineContext';

/** The id of the model transcription will use, or null when none is chosen; `loading` until the engine has answered. */
export function useActiveSpeechModel(): { loading: boolean; activeId: string | null } {
  const engine = useSpeechEngine();
  const [state, setState] = useState<{ loading: boolean; activeId: string | null }>({
    loading: true,
    activeId: null,
  });
  useEffect(() => {
    const read = () =>
      void engine.activeModelId().then(
        (activeId) => setState({ loading: false, activeId }),
        () => setState({ loading: false, activeId: null }),
      );
    read();
    return engine.subscribe(read);
  }, [engine]);
  return state;
}
