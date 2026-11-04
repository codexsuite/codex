/** `1.0.0+abc123` -> `1.0.0`: the build metadata after `+` is the commit, not part of the version people read. */
export function displayVersion(version: string): string {
  const plus = version.indexOf('+');
  return plus < 0 ? version : version.slice(0, plus);
}
