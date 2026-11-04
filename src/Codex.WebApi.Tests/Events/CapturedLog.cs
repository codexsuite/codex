using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Events;

// One log line a host wrote, as CapturedLogs saw it.
public sealed record CapturedLog(string Category, LogLevel Level, string Message);
