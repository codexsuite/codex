using Codex.Core;

namespace Codex.UnitTest;

[TestFixture]
public class DisplayDateTests
{
    [Test]
    public void Format_WhenUtc_ReturnsMinutePrecisionUtcLabel()
    {
        var value = new DateTimeOffset(2026, 1, 2, 3, 4, 59, TimeSpan.Zero);

        DisplayDate.Format(value).ShouldBe("2026-01-02 03:04 UTC");
    }

    [Test]
    public void Format_WhenOffsetGiven_ConvertsToUtcFirst()
    {
        var value = new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.FromHours(10));

        DisplayDate.Format(value).ShouldBe("2026-01-01 17:04 UTC");
    }

    [TestCase("2026-10-19T14:59:40Z", "just now")]
    [TestCase("2026-10-19T15:00:30Z", "just now")]
    [TestCase("2026-10-19T14:48:00Z", "12m ago")]
    [TestCase("2026-10-19T13:00:00Z", "2h ago")]
    [TestCase("2026-10-19T00:00:00Z", "15h ago")]
    [TestCase("2026-10-18T23:59:00Z", "Yesterday")]
    [TestCase("2026-10-18T00:00:00Z", "Yesterday")]
    [TestCase("2026-09-30T10:00:00Z", "30 Sep")]
    [TestCase("2026-01-01T00:00:00Z", "1 Jan")]
    [TestCase("2025-03-02T10:00:00Z", "2 Mar 2025")]
    public void Relative_WhenEditedAt_ShouldReadLikeTheLibrary(string edited, string expected)
    {
        var now = DateTimeOffset.Parse("2026-10-19T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        DisplayDate.Relative(DateTimeOffset.Parse(edited, System.Globalization.CultureInfo.InvariantCulture), now, TimeZoneInfo.Utc)
            .ShouldBe(expected);
    }

    [Test]
    public void Relative_WhenZoneIsAhead_ShouldUseItsCalendarDay()
    {
        var now = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus10", TimeSpan.FromHours(10), "plus10", "plus10");

        // Midnight UTC on the 19th is 10:00 local on the 19th, and now is 01:00 local on the 20th
        // (in UTC the same pair reads "15h ago").
        DisplayDate.Relative(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), now, zone).ShouldBe("Yesterday");
    }

    [Test]
    public void Today_WhenZoneIsAhead_ShouldBeItsCalendarDay()
    {
        var now = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus10", TimeSpan.FromHours(10), "plus10", "plus10");

        DisplayDate.Today(now, TimeZoneInfo.Utc).ShouldBe(new DateOnly(2026, 10, 19));
        DisplayDate.Today(now, zone).ShouldBe(new DateOnly(2026, 10, 20));
    }

    [TestCase("2026-10-19T15:00:00Z", 0)]
    [TestCase("2026-10-19T23:59:00Z", 0)]
    [TestCase("2026-10-20T00:00:00Z", 1)]
    [TestCase("2026-10-29T08:00:00Z", 10)]
    [TestCase("2026-10-18T23:59:00Z", -1)]
    public void DaysUntil_WhenDueAt_ShouldCountCalendarDays(string due, int expected)
    {
        var now = new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

        DisplayDate.DaysUntil(DateTimeOffset.Parse(due, System.Globalization.CultureInfo.InvariantCulture), now, TimeZoneInfo.Utc)
            .ShouldBe(expected);
    }

    [Test]
    public void StartOfDay_WhenNoTransition_ShouldBeLocalMidnightInUtc()
    {
        DisplayDate.StartOfDay(new DateOnly(2026, 6, 1), MidnightTransitions())
            .ShouldBe(new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void StartOfDay_WhenMidnightIsSkipped_ShouldBeTheTransition()
    {
        // Clocks go from 00:00 (-03) to 01:00 (-02) on 1 Nov, so the day starts at 03:00Z, which reads 01:00 locally.
        var start = DisplayDate.StartOfDay(new DateOnly(2026, 11, 1), MidnightTransitions());

        start.ShouldBe(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero));
        start.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void StartOfDay_WhenMidnightHappensTwice_ShouldBeTheFirstMidnight()
    {
        // Clocks go from 01:00 (-02) back to 00:00 (-03) on 21 Feb, so midnight happens at 02:00Z and again at 03:00Z.
        DisplayDate.StartOfDay(new DateOnly(2026, 2, 21), MidnightTransitions())
            .ShouldBe(new DateTimeOffset(2026, 2, 21, 2, 0, 0, TimeSpan.Zero));
    }

    // A -03 zone with summer time (-02) from midnight on 1 Nov until 01:00 summer time on 21 Feb: both transitions touch
    // midnight, as some real zones' do.
    private static TimeZoneInfo MidnightTransitions()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), 11, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0, DateTimeKind.Unspecified), 2, 21));
        return TimeZoneInfo.CreateCustomTimeZone("midnight-dst", TimeSpan.FromHours(-3), "midnight-dst", "standard", "summer", [rule]);
    }
}
