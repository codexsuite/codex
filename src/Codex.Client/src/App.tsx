import { AuthGate } from './features/auth/AuthGate';
import { Shell } from './shell/Shell';

// Every page but /login: signed in (AuthGate), then the shell.
export function App() {
  return (
    <AuthGate>
      <Shell />
    </AuthGate>
  );
}
