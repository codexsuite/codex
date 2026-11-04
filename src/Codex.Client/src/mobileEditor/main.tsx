import '@fontsource-variable/inter';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MobileEditor } from './MobileEditor';
import './tokens.css';
import './mobileEditor.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MobileEditor />
  </StrictMode>,
);
