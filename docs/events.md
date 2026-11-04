# Book events across processes

`IBookEvents` (`Codex.Contracts.Events`) is one bus per process: `InMemoryBookEvents` in `Codex.Application/Events/`
delivers each published event to that process's subscribers (the SignalR relay `BookEventsRelay`, the quote-flag
scanner). Three processes publish: the web host, the stdio MCP server (`src/Codex.Mcp`) and the worker container. A
Postgres LISTEN/NOTIFY bridge carries events from the last two to the web host, so an agent's edit shows *Updated
elsewhere* and raises quote flags, and job progress reaches the browser. Publishers and subscribers do nothing extra:
publish through `IBookEvents` after your write commits, as before.

## How it works

- **Send (every process).** After delivering locally, `PublishAsync` hands the event to `BookEventsNotifier`, which runs
  `pg_notify('codex_book_events', payload)` on a pooled connection. The payload is
  `{"origin": <process instance id>, "event": <the BookEvent JSON, "type" discriminator and clientId included>}`.
  Inside an ambient `TransactionScope` the connection enlists, so the notification goes out only on commit (local
  delivery still happens at once). Sending is bounded by 2 s. A payload over 7999 bytes (NOTIFY's limit is 8000) is
  logged and not sent, and so is any failure. The publisher never sees an error. A save of n scenes sends n
  notifications, one after another.
- **Receive (listening processes only).** `BookEventsListener` (a hosted service) holds one dedicated, unpooled
  connection (`application_name = codex-events-listener`) that runs `LISTEN codex_book_events`. It hands each event from
  another process to the local subscribers only, so nothing is sent on again. It skips its own events by instance id,
  since its subscribers already had them, and re-checks `clientId` as the `X-Codex-Client` header is checked. A payload
  that is not a book event is logged and skipped.
- **Reconnect.** Any failure reopens the connection with back-off (1 s doubling to 30 s). The back-off and the outage end
  only once the new connection has proved itself (a notification arrived, or it stayed up 60 s). Each outage logs one
  warning and logs its repeats at Debug. Events sent while the listener is down are not replayed.
- **Scope.** NOTIFY is per database, so processes on different databases, test clones included, never hear each other.

## `CODEX_EVENTS_LISTEN` (default `true`)

Whether a process runs the listener. Every process sends either way.

| Process | Listens | How |
|---|---|---|
| Web host | yes | the default |
| Worker (same `Codex.WebApi.dll`) | no | compose sets `CODEX_EVENTS_LISTEN=false` on the worker service; if it listened, its own quote-flag scanner would rescan every save |
| Stdio MCP server | no | its `Program.cs` drops all hosted services, so the value does not matter |

## Tests

`Codex.WebApi.Tests/Events/`: a second `CodexHost` on the same cloned database with
`services.AddSingleton(new BookEventsSettings(Listen: false))` stands in for the MCP server or the worker. "Nothing else
arrived" is proved without waiting: notifications reach the listener in commit order, so a marker sent afterwards shows
up after anything delivered wrongly. `CapturedLogs.ReplaceLoggingIn` swaps a host's logger factory (Serilog ignores
other providers), so a test can wait for a log line.
