---
name: utc-everywhere
description: The date and time rule for Codex. Everything from the BFF back is UTC, and time zones exist only at the last mile, where the BFF renders a display string. Use when writing or reviewing anything that touches a date, time or timestamp: a column or migration, SQL with now() or a day boundary, a C# DateTimeOffset/DateOnly, a DTO field or display label, an endpoint or MCP tool that accepts a date, a browser date input, or server, container or Postgres time settings.
---

# UTC everywhere

**Every date-time from the BFF back is UTC. No exceptions.** The database, SQL, services, the worker, events, jobs, MCP, logs, the servers, the containers and Postgres all run on UTC. No time zone is stored, carried, passed or computed with anywhere behind the BFF. UTC is the assumption, not a label: a value carries no zone because it never needs one.

A time zone appears in exactly **one** place: the **last mile**, where the BFF (`Codex.WebApi` and the Application services behind it) turns a UTC instant into a display string for the viewer. Its mirror is the **inbound edge**, where the BFF validates a date the browser sent and converts it to UTC. Those are the only two places where zone code belongs.

**Why this is non-negotiable:** once a zone gets into the backend, daylight saving makes values travel through time. An hour repeats, an hour vanishes, "midnight" doesn't exist on some days, and two machines disagree about what day it is. Every conversion is a bug waiting to happen. With one zone there are no conversions to get wrong.

## Where each layer stands

| Layer | The rule |
|---|---|
| Servers, containers, tests, Postgres | Run on UTC whatever the machine's own zone: `TZ=UTC` (the process's zone) and `PGTZ=UTC` (each Postgres session's `TimeZone`) are pinned in both Dockerfiles, `docker-compose.yml`, `src/codex.runsettings` (every `dotnet test` process) and `launchSettings.json` (`dotnet run`); acceptance browser contexts use `TimezoneId = "UTC"`. A new process, image or test entry point gets the same pins. Code still never relies on the host's zone: it asks `TimeProvider` for UTC. |
| Database columns | An instant is `timestamptz`. Despite the name it **stores no zone**: Postgres normalises the value to a UTC instant and keeps only that. A calendar day with no time of day (`accessed_on`) is `date`. Never add a zone-name or offset column, and never use `timestamp without time zone` to hold a wall-clock time. |
| SQL | Stamp with `now()`, and do arithmetic with `interval` (`now() + interval '14 days'`). A day boundary (`current_date`, `ts::date`, `date_trunc('day', ts)`) depends on the session's `TimeZone`: pass the day from C# as a parameter, or write `(ts AT TIME ZONE 'UTC')::date` so the zone is fixed. |
| C# | The clock is `TimeProvider.GetUtcNow()` (`BannedSymbols.txt` bans `DateTime.Now`/`UtcNow`). Instants are `DateTimeOffset` with offset zero. Pass UTC to Npgsql, which rejects any other offset; `DapperTypeHandlers` reads `timestamptz` back as offset zero. A calendar day is `DateOnly`. `TimeZoneInfo`, `ToLocalTime()` and `clock.LocalTimeZone` belong only at the last mile and the inbound edge. |
| Events, jobs, MCP results, logs | UTC instants. Labels are the last mile's job, so they are built there and nowhere else. |
| BFF → browser (last mile) | A DTO carries the UTC value as an ISO string (`lastEditedAt`) **plus** a server-formatted label (`lastEditedLabel`) from `Codex.Core.DisplayDate`, converted to the viewer's zone. That conversion is the only outbound zone code in the system. |
| Browser | Renders the label. It treats ISO and `yyyy-MM-dd` strings as **opaque**: it compares and sorts them as strings (they sort correctly) and sends them back unchanged. It never makes a `Date`, imports a date library, calls `Intl.DateTimeFormat`/`toLocale*String` on a date, or does date arithmetic. If it needs "next week" or "is this overdue", the BFF computes it and sends the answer. |
| Browser → BFF (inbound edge) | Data entry is the one legitimate case of date handling in the browser: an `<input type="date">` value goes up as the `yyyy-MM-dd` string it already is. Then the BFF validates it hard (below). |

**The viewer's zone** is a parameter of the `DisplayDate` functions. Users have no time-zone setting yet, so every call site passes `clock.LocalTimeZone`, which is UTC on UTC servers. When a per-user setting lands, it replaces that argument at the last mile and the inbound edge, and nowhere else changes. Day arithmetic in the viewer's zone goes through `DisplayDate` too: `Today(now, zone)` (an entered day's bound), `DayOf(value, zone)` (an instant's day, e.g. a date input's value), `DaysUntil(value, now, zone)` ("due in 3 days") and `StartOfDay(day, zone)` (an entered day as a UTC instant). Don't hand-write `TimeZoneInfo.ConvertTime` in a service.

## Accepting a date at the inbound edge

Anything that comes out of the browser (or an MCP caller) is untrusted. Validate dates as harshly as passwords. For every date field an endpoint or tool accepts:

1. **The request field is a `string`**, not `DateTime`/`DateTimeOffset`. Model binding accepts any culture's format and any offset, and that is exactly the looseness that lets a zone in.
2. **Parse with one exact format**: `DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)`. A time of day is a separate `HH:mm` string, parsed the same way. Never use `DateTime.Parse`, `Convert.ToDateTime`, a culture-sensitive overload, or a fallback list of formats.
3. **Bound it** to what the feature allows (not in the past, at most a year out, a sane year range) and reject anything else with `ApiException.Validation` (`McpToolException.Invalid` on MCP), naming the expected format: "The reminder date must be written yyyy-MM-dd."
4. **Convert to UTC once, right here.** A pure calendar day stays a `DateOnly` and is stored as `date`. A day or wall-clock time that marks a moment ("remind me on 30 Oct") is in the viewer's zone: convert it to a UTC `DateTimeOffset` with that zone in this one line (`DisplayDate.StartOfDay` for a day). A wall-clock time can fall in a daylight-saving gap or overlap (`TimeZoneInfo.IsInvalidTime` / `IsAmbiguousTime`), so reject it or resolve it on purpose: `StartOfDay` takes the first instant of the day when midnight is skipped or repeated. After this line no zone exists. `PermissionCommands.PatchAsync` (the permission reminder) is the worked example.
5. **Test the edge**, in the feature's API tests: wrong format (`30/10/2026`, `2026-10-30T00:00`, a trailing offset), an impossible date (`2026-02-30`), an out-of-range date, and the happy path. Compute expected values from the run's clock (time is never frozen, D4).

## Red flags

Each of these lets a zone in. Use the fix instead.

| Pattern | Fix |
|---|---|
| `new Date(...)`, `Date.now()`, `Date.parse`, dayjs/date-fns/moment, `Intl.DateTimeFormat` in the SPA | A server label, or a server-computed field |
| A column named `*_tz`, `timezone`, `utc_offset`, or `timestamp without time zone` holding a local time | `timestamptz` (a UTC instant) or `date` (a calendar day) |
| `current_date`, `::date`, `date_trunc('day', …)` in SQL with no explicit zone | A day passed from C#, or `AT TIME ZONE 'UTC'` |
| `DateTimeOffset` with a non-zero offset, `DateTime` of kind `Local`/`Unspecified`, `ToLocalTime()` | `clock.GetUtcNow()`, `DateOnly` for days |
| `TimeZoneInfo` or `LocalTimeZone` outside `DisplayDate` and an inbound-edge conversion | Move it to the last mile |
| A DTO date-time field typed `DateTime`/`DateTimeOffset` on a **request** | `string`, parsed by the steps above |
| A label built in the browser or stored in the database | `DisplayDate` at the last mile |

**When reviewing**, account for every date in the diff: each new column, SQL expression, DTO field, `TimeZoneInfo` use and browser date reference sits in a layer of the table above and follows its rule. This lists the candidates:

```bash
git diff origin/main... | grep -nE '^\+.*(timestamp|::date|current_date|date_trunc|AT TIME ZONE|DateTime|DateOnly|TimeZoneInfo|LocalTimeZone|ToLocalTime|new Date|Date\.|Intl\.|type="(date|time|datetime-local)")'
```
