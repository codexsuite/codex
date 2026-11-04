import { QueryClient } from '@tanstack/react-query';

// The app's one QueryClient. A module singleton so the platform's lost-session handler can empty it.
export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});
