using Microsoft.Extensions.Configuration;

namespace Codex.Application.Events;

// CODEX_EVENTS_LISTEN (default true): whether this process runs BookEventsListener and so hears book events that other
// processes on the same database published. Every process publishes regardless. The web host listens (it holds the
// SignalR clients and the quote-flag scanner). The worker runs the same host, so it listens unless its environment sets
// this false; it should, or its own quote-flag scanner rescans every save. The stdio MCP server never starts hosted
// services, so it never listens whatever the value.
public sealed record BookEventsSettings(bool Listen)
{
    public const string ListenKey = "CODEX_EVENTS_LISTEN";

    public static BookEventsSettings From(IConfiguration configuration) => new(configuration.GetValue(ListenKey, true));
}
