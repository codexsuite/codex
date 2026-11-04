using System.Globalization;

namespace Codex.Core;

// Pure, dependency-free logic lives in this project; Codex.Application, Codex.WebApi and the test projects
// reference it. The SPA never builds Date objects, so every date it shows is formatted here
// (.claude/skills/utc-everywhere): this is the last mile, where a UTC instant meets the viewer's time zone.
// Tests compute expected labels with the same functions and the run's clock (D4: time is never frozen).
public static class DisplayDate
{
    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    // "Last edited" style label (#16 Part 1, design/README.md shelf tile): "just now", "12m ago", "2h ago"
    // (same calendar day), "Yesterday", "30 Sep" (this year), "2 Mar 2025" (an earlier year). Calendar days are
    // the clock's local time zone; a value in the future (clock skew) reads "just now".
    public static string Relative(DateTimeOffset value, TimeProvider clock) =>
        Relative(value, clock.GetUtcNow(), clock.LocalTimeZone);

    public static string Relative(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone)
    {
        var elapsed = now - value;
        var day = TimeZoneInfo.ConvertTime(value, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone);
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }
        if (day.Date == today.Date)
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }
        if (day.Date == today.Date.AddDays(-1))
        {
            return "Yesterday";
        }
        return ShortDay(value, now, zone);
    }

    // The time of day in the clock's local time zone ("14:02"), for lines such as "the version from 14:02".
    public static string Clock(DateTimeOffset value, TimeProvider clock) =>
        TimeZoneInfo.ConvertTime(value, clock.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    // Today's calendar day in the viewer's zone: the "not in the past" / "not after today" bound for an entered day.
    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo zone) => DayOf(now, zone);

    // The calendar day an instant falls on in the viewer's zone (a date input's value, "yyyy-MM-dd").
    public static DateOnly DayOf(DateTimeOffset value, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);

    // Whole calendar days from today to the day value falls on, both in the viewer's zone ("due in 3 days").
    public static int DaysUntil(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone) =>
        DayOf(value, zone).DayNumber - Today(now, zone).DayNumber;

    // The inbound edge's conversion of an entered day to the UTC instant it starts at in the viewer's zone. Some zones
    // change clocks at midnight, so midnight can be skipped (a gap: the day starts at the transition, read with the
    // offset in force before it) or happen twice (an overlap: the day starts at the first midnight, the larger offset).
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        var offset = zone.GetUtcOffset(midnight);
        if (zone.IsInvalidTime(midnight))
        {
            offset = zone.GetUtcOffset(midnight.AddTicks(-1));
        }
        else if (zone.IsAmbiguousTime(midnight))
        {
            offset = zone.GetAmbiguousTimeOffsets(midnight).Max();
        }

        return new DateTimeOffset(midnight, offset).ToUniversalTime();
    }

    // A calendar day as the UI prints it ("Cleared · JD, 19 Oct"): "19 Oct" this year, "2 Mar 2025" otherwise.
    public static string ShortDay(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone)
    {
        var day = TimeZoneInfo.ConvertTime(value, zone);
        var format = day.Year == TimeZoneInfo.ConvertTime(now, zone).Year ? "d MMM" : "d MMM yyyy";
        return day.ToString(format, CultureInfo.InvariantCulture);
    }
}
