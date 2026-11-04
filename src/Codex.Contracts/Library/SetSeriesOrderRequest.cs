namespace Codex.Contracts.Library;

// Every live book of the series, in the new order.
public sealed record SetSeriesOrderRequest(IReadOnlyList<Guid>? BookIds);
