import { beforeMount } from '@playwright/experimental-ct-react/hooks';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { configurePlatform } from '@codex/core/platform';
import { webPlatform } from '../src/webPlatform';
import '../src/index.css';

configurePlatform(webPlatform);

type HooksConfig = {
  route?: string;
};

// Every mounted component gets the app's providers: a fresh query cache (no retries, so a stubbed error
// shows at once) and a router at hooksConfig.route.
beforeMount<HooksConfig>(async ({ App, hooksConfig }) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[hooksConfig?.route ?? '/']}>
        <App />
      </MemoryRouter>
    </QueryClientProvider>
  );
});
