import { createContext, useContext } from 'react';
import type { Marker } from '@codex/core/api/references';

/** Opens the marker card at an element; the page owns the single card. */
export type OpenMarkerCard = (marker: Marker, anchor: HTMLElement) => void;

export const MarkerCardContext = createContext<OpenMarkerCard>(() => {});

export const useOpenMarkerCard = () => useContext(MarkerCardContext);
