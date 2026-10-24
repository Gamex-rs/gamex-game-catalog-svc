namespace Gamex.GameCatalog.Infrastructure.Jobs;

internal sealed class NightlySchedule(TimeOnly localTime, TimeZoneInfo timeZone)
{
    public TimeOnly LocalTime { get; } = localTime;
    public TimeZoneInfo TimeZone { get; } = timeZone;

    /// <summary>
    /// Next occurrence of <see cref="LocalTime"/> in <see cref="TimeZone"/> strictly after <paramref name="nowUtc"/>.
    /// A local time skipped by a DST jump runs at the first valid instant after it; a repeated one runs once (first occurrence).
    /// </summary>
    public DateTime NextRunUtc(DateTime nowUtc)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZone);
        var date = DateOnly.FromDateTime(nowLocal);

        for (var i = 0; i < 3; i++)
        {
            var candidate = ToUtc(date.AddDays(i).ToDateTime(LocalTime));
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No run time found within three days.");
    }

    private DateTime ToUtc(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (TimeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (TimeZone.IsAmbiguousTime(local))
        {
            // First occurrence = the earlier UTC instant = the larger (daylight) offset.
            var offset = TimeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
    }
}
