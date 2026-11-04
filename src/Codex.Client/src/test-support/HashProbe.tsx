import { useLocation } from 'react-router-dom';

/** Shows the memory router's current hash so a component test can assert URL changes. */
export function HashProbe() {
  const { hash } = useLocation();
  return <output data-testid="test-hash-probe">{hash}</output>;
}
