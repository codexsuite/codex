import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { configurePlatform } from '@codex/core/platform';
import { AppRoutes } from './AppRoutes';
import { initTheme } from './shell/theme';
import { webPlatform } from './webPlatform';
import './index.css';

configurePlatform(webPlatform);
initTheme();

// One query cache for the app; component tests mount their own (playwright/index.tsx).
const queryClient = new QueryClient();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
