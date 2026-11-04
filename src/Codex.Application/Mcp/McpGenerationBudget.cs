using System.Collections.Concurrent;
using System.Globalization;
using Agoda.IoC.Core;
using Microsoft.Extensions.Configuration;

namespace Codex.Application.Mcp;

// The cap on Codex generations an MCP client can start (M9, #32): CODEX_MCP_MAX_GENERATIONS, 20 by default; the next one
// is rate_limited. Only draft_prose counts (it spends the author's model budget); submit_draft does not. The setting is
// read when the budget is first resolved, not at registration, so a test override is honoured. Counts live in memory:
//   - a call with a personal access token counts against that token, over a sliding hour;
//   - otherwise a stdio process is one session, with that many for its life;
//   - otherwise (stateless HTTP from this machine) every such client shares one allowance over a sliding hour.
[RegisterSingleton]
public sealed class McpGenerationBudget(IConfiguration configuration, TimeProvider clock)
{
    public const string Setting = "CODEX_MCP_MAX_GENERATIONS";

    public const int DefaultLimit = 20;

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private const string Stateless = "stateless";
    private const string TokenPrefix = "token:";

    private readonly ConcurrentDictionary<string, int> _used = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);

    public int Limit { get; } = int.TryParse(configuration[Setting], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
        ? n
        : DefaultLimit;

    // Takes one generation from the caller's budget, or fails rate_limited. Returns the key to Release when the
    // generation could not start.
    public string Reserve(McpCaller caller)
    {
        if (caller.TokenId is { } token)
        {
            return ReserveWindow(TokenPrefix + token.ToString("N", CultureInfo.InvariantCulture),
                $"This token has started {Limit} Codex generations in the last hour. Use submit_draft to supply text yourself, or try later.");
        }
        if (caller.Session is not { } session)
        {
            return ReserveWindow(Stateless,
                $"{Limit} Codex generations were started in the last hour over HTTP. Use submit_draft to supply text yourself, or try later.");
        }
        var taken = false;
        _used.AddOrUpdate(session, _ =>
        {
            taken = true;
            return 1;
        }, (_, used) =>
        {
            taken = used < Limit;
            return taken ? used + 1 : used;
        });
        return taken
            ? session
            : throw new McpToolException(McpErrorCodes.RateLimited,
                $"This session has used its {Limit} Codex generations. Use submit_draft to supply text yourself, or start a new session.");
    }

    public void Release(string key)
    {
        if (_recent.TryGetValue(key, out var recent))
        {
            lock (recent)
            {
                if (recent.Count > 0)
                {
                    var kept = recent.ToArray()[..^1];
                    recent.Clear();
                    Array.ForEach(kept, recent.Enqueue);
                }
            }
            return;
        }
        _used.AddOrUpdate(key, 0, (_, used) => Math.Max(0, used - 1));
    }

    public int Remaining(string key)
    {
        if (!_recent.TryGetValue(key, out var recent))
        {
            return Math.Max(0, Limit - _used.GetValueOrDefault(key));
        }
        lock (recent)
        {
            Trim(recent);
            return Math.Max(0, Limit - recent.Count);
        }
    }

    private string ReserveWindow(string key, string refusal)
    {
        var recent = _recent.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (recent)
        {
            Trim(recent);
            if (recent.Count >= Limit)
            {
                throw new McpToolException(McpErrorCodes.RateLimited, refusal);
            }
            recent.Enqueue(clock.GetUtcNow());
            return key;
        }
    }

    private void Trim(Queue<DateTimeOffset> recent)
    {
        var cutoff = clock.GetUtcNow() - Window;
        while (recent.Count > 0 && recent.Peek() <= cutoff)
        {
            recent.Dequeue();
        }
    }
}
