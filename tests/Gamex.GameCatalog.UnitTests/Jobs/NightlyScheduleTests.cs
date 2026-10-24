using Gamex.GameCatalog.Infrastructure.Jobs;

namespace Gamex.GameCatalog.UnitTests.Jobs;

public sealed class NightlyScheduleTests
{
    private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void BeforeRunTime_RunsSameDay()
    {
        // 01:00 UTC = 03:00 CEST in summer; 00:30 UTC is 02:30 local.
        var schedule = new NightlySchedule(new TimeOnly(3, 0), Belgrade);

        schedule.NextRunUtc(Utc(2026, 7, 1, 0, 30)).ShouldBe(Utc(2026, 7, 1, 1));
    }

    [Fact]
    public void AtOrAfterRunTime_RunsNextDay()
    {
        var schedule = new NightlySchedule(new TimeOnly(3, 0), Belgrade);

        schedule.NextRunUtc(Utc(2026, 7, 1, 1)).ShouldBe(Utc(2026, 7, 2, 1));
        schedule.NextRunUtc(Utc(2026, 7, 1, 12)).ShouldBe(Utc(2026, 7, 2, 1));
    }

    [Fact]
    public void Winter_UsesStandardOffset()
    {
        // 03:00 CET = 02:00 UTC.
        var schedule = new NightlySchedule(new TimeOnly(3, 0), Belgrade);

        schedule.NextRunUtc(Utc(2026, 12, 1, 12)).ShouldBe(Utc(2026, 12, 2, 2));
    }

    [Fact]
    public void SpringForward_SkippedLocalTime_RunsAtFirstValidInstant()
    {
        // 2027-03-28: clocks jump 02:00 → 03:00, so 02:30 does not exist; run at 03:00 CEST = 01:00 UTC.
        var schedule = new NightlySchedule(new TimeOnly(2, 30), Belgrade);

        schedule.NextRunUtc(Utc(2027, 3, 27, 12)).ShouldBe(Utc(2027, 3, 28, 1));
    }

    [Fact]
    public void FallBack_RepeatedLocalTime_RunsOnceAtFirstOccurrence()
    {
        // 2026-10-25: clocks go 03:00 → 02:00, so 02:30 happens twice; first is 02:30 CEST = 00:30 UTC.
        var schedule = new NightlySchedule(new TimeOnly(2, 30), Belgrade);

        var first = schedule.NextRunUtc(Utc(2026, 10, 24, 12));
        first.ShouldBe(Utc(2026, 10, 25, 0, 30));

        // From the first run, the next is the following day, not the repeated 02:30 one hour later.
        schedule.NextRunUtc(first).ShouldBe(Utc(2026, 10, 26, 1, 30));
    }
}
