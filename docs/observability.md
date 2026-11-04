# Observability

Codex exports traces, metrics and logs over OpenTelemetry (OTLP) when you point it at a collector. Console logging and Seq are unchanged and work with or without it.

## Turning it on

Set the standard OpenTelemetry variables, in the environment (the endpoint is read while the host is built, so appsettings is too late). Nothing is registered, and nothing leaves the process, until `OTEL_EXPORTER_OTLP_ENDPOINT` is set. This applies to the web host and the worker alike.

| Variable | Meaning |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Where to send. Setting it turns telemetry on for all three signals. |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` or `http/protobuf`. Logs use the `/v1/logs` path with `http/protobuf`. |
| `OTEL_EXPORTER_OTLP_HEADERS` | `key=value` pairs, comma separated, for a backend's credentials. |
| `OTEL_EXPORTER_OTLP_TIMEOUT` | Milliseconds an export may take before it is given up. Default 10000; the log sink honours it too. |
| `OTEL_SERVICE_NAME` | Defaults to `codex`. The box sets `codex-worker` on the worker container. |

Every signal carries the same resource: `service.name`, `service.version` (the build's informational version, as `/health` reports it) and `service.instance.id` (host name plus a per-process id, so a restart is a new instance). The collector adds `host.name` and `deployment.environment`.

The tracer and meter use the one name `Codex` for custom spans and instruments (`Codex.Application.Telemetry.CodexTelemetry`).

Exporting is batched and off the request path: a collector that is down or never answers costs the app nothing but the dropped telemetry. Queues are bounded (the log sink holds at most 10000 records).

## Traces

- **ASP.NET Core**: one server span per request, named by route template (`GET /api/books/{id:guid}`), never the raw path. `url.path` and `url.query` are not recorded. Only `/api` and `/mcp` requests are traced.
- **Npgsql**: a child span per database command. The SQL text is recorded, parameter values are not.
- **HttpClient**: outgoing calls (the user's AI provider, and the Open Library and Crossref reference lookups). `url.full` is scheme, host and path only: no credentials, no query string. The `Authorization` header is never recorded.
- **Codex's own spans** (the `Codex` source): `mcp.tool_call` and the `job.*` spans of the export, proof and asset rendition workers.
- **Sampling**: parent-based always-on, so `OTEL_TRACES_SAMPLER` is not honoured. A root client span (Npgsql or HttpClient with no request or job span around it) is dropped. The print workers and the asset worker poll the database every 2 to 5 seconds, the book-event listener keeps its own connection and startup migrates; kept, those would be a trace of one polling query every couple of seconds. The same work inside a request or a job span is a child and is kept.
- **Left out of tracing on purpose**: `/api/health` and `/health` (container probe), the SignalR hub `/hubs/book` (a negotiate and one long-lived connection per open tab, covered by framework metrics) and the SPA's assets and fallback pages. `/mcp` is stateless (`GET` and `DELETE` answer 405), so there is no long-lived stream to span.

## Metrics

Built in: ASP.NET Core (`Microsoft.AspNetCore.Hosting`, Kestrel, `Microsoft.AspNetCore.Http.Connections` for SignalR), `System.Net.Http`, the .NET runtime and Npgsql.

The `Codex` meter:

| Instrument | Kind | Tags |
|---|---|---|
| `codex.mcp.tool_calls` | counter | `tool`, `outcome` |
| `codex.mcp.tool_duration` | histogram, seconds | `tool`, `outcome` |
| `codex.jobs` | counter | `job` (`export`, `proof`, `asset_rendition`), `outcome` |
| `codex.job_duration` | histogram, seconds | `job`, `outcome` |

Tags stay bounded: a registered tool name, a job kind, an outcome. No book, scene or user ids, titles or prose.

## Logs

Logs go over OTLP through Serilog, in addition to the console and Seq sinks, using the same endpoint, protocol, headers and resource. Records go in batches of up to 100 every 2 seconds. Each log record written during a request carries that request's `TraceId` and `SpanId`, so a log line links to its trace in the backend (and in Seq). The sink's own POSTs are not traced. Do not set `CODEX_SEQ_URL` on a host that sends OTLP to a collector that forwards to Seq: the Seq sink posts directly, so every record would arrive twice.

## Production

In production, the apps send to an OpenTelemetry collector, which forwards to Seq and VictoriaMetrics.

## Deliberately left out

- **PII**: no email addresses, names or user ids as span attributes or metric tags.
- **Secrets**: no tokens, passwords, AI provider keys or `Authorization` headers.
- **Query strings**: sign-in and OAuth parameters and a provider key in a URL must not leak, so the query string is never recorded.
- **SQL parameter values**: statements only.
- **Manuscript content**: titles, prose, summaries and knowledge entries are never attributes or tags.
