import { Link } from 'react-router-dom';

// The dashed 2:3 tile that ends a shelf (design/screens/library.html `.addtile`).
export function AddTile({ to, label, testId }: { to: string; label: string; testId: string }) {
  return (
    <Link
      to={to}
      data-testid={testId}
      className="grid aspect-[2/3] w-[var(--cover-width)] place-items-center rounded-xs border border-dashed border-border text-xs text-muted no-underline outline-offset-1 hover:border-accent hover:text-accent focus-visible:outline-2 focus-visible:outline-accent"
    >
      {label}
    </Link>
  );
}
