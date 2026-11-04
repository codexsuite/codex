import { useState } from 'react';
import { ApiRequestError } from '@codex/core/api/client';
import { categoryColours } from './categoryColours';
import { kbTestIds } from './testIds';

interface NewCategoryFormProps {
  /** Creates the category; rejects with the server's message. */
  onCreate: (name: string, colour: string) => Promise<unknown>;
}

// Name and one of the `--category-*` colours.
export function NewCategoryForm({ onCreate }: NewCategoryFormProps) {
  const [name, setName] = useState('');
  const [colour, setColour] = useState<string>(categoryColours[0]);
  const [error, setError] = useState<string | null>(null);

  const create = async () => {
    try {
      await onCreate(name, colour);
    } catch (e) {
      setError(e instanceof ApiRequestError ? e.message : 'The category could not be created.');
    }
  };

  return (
    <div data-testid={kbTestIds.newCategory} className="flex flex-col gap-2 p-2">
      <input
        type="text"
        data-testid={kbTestIds.newCategoryName}
        aria-label="Category name"
        placeholder="Name, for example Places"
        value={name}
        onChange={(event) => setName(event.target.value)}
        className="h-7 rounded border border-border bg-bg px-2 text-sm text-text"
      />
      <div className="flex gap-2" role="group" aria-label="Colour">
        {categoryColours.map((token) => (
          <button
            key={token}
            type="button"
            data-testid={kbTestIds.newCategoryColour(token)}
            aria-label={token}
            aria-pressed={colour === token}
            onClick={() => setColour(token)}
            className="h-5 w-5 rounded-full border-2 border-transparent aria-pressed:border-text"
            style={{ background: `var(--${token})` }}
          />
        ))}
      </div>
      {error && (
        <p
          data-testid={kbTestIds.newCategoryError}
          role="alert"
          className="m-0 text-xs text-danger"
        >
          {error}
        </p>
      )}
      <button
        type="button"
        data-testid={kbTestIds.newCategoryCreate}
        onClick={() => void create()}
        className="h-7 self-end rounded border-0 bg-accent px-3 text-sm text-white"
      >
        Create
      </button>
    </div>
  );
}
