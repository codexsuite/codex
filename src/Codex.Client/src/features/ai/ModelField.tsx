import { field } from '../auth/styles';

interface ModelFieldProps {
  testId: string;
  label: string;
  /** The ids the provider listed; empty means the author types one. */
  models: string[];
  value: string;
  onChange: (value: string) => void;
}

// A default model: a select over the provider's listed models, or a text input when it listed none.
export function ModelField({ testId, label, models, value, onChange }: ModelFieldProps) {
  if (models.length === 0) {
    return (
      <input
        data-testid={testId}
        aria-label={label}
        placeholder="Model id"
        autoComplete="off"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={field}
      />
    );
  }
  const options = value === '' || models.includes(value) ? models : [value, ...models];
  return (
    <select
      data-testid={testId}
      aria-label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={field}
    >
      {value === '' && <option value="">Choose a model</option>}
      {options.map((m) => (
        <option key={m} value={m}>
          {m}
        </option>
      ))}
    </select>
  );
}
