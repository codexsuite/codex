namespace Codex.Contracts.Settings;

// One citation/permission value as the book sees it. Inherited is true while the book is in a series and has no value
// of its own (the series' value, or the default, shows through); setting a value of its own makes it false.
public sealed record PublicationValueDto<T>(T Value, bool Inherited);
