using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.Telemetry;

// Writes one log inside the request and returns the request's trace and span ids. Top level because MVC's controller
// discovery skips nested types; only loaded by the telemetry tests that add this assembly as an application part of
// their own test host.
[Route("api/test-controller/log-probe")]
public sealed class LogProbeTestController(ILogger<LogProbeTestController> logger) : ApiControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        logger.LogWarning("probe inside request");
        var activity = System.Diagnostics.Activity.Current;
        return Ok(new LogProbeResult(activity?.TraceId.ToHexString() ?? "none", activity?.SpanId.ToHexString() ?? "none"));
    }

    public sealed record LogProbeResult(string TraceId, string SpanId);
}
