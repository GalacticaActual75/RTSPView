namespace RTSPView.Core;

public sealed record RestartScheduleSettings
{
    public bool Enabled { get; init; }
    public string Action { get; init; } = "viewer";
    public string Mode { get; init; } = "weekly";
    public int IntervalHours { get; init; } = 24;
    public int[] Days { get; init; } = [0];
    public string Time { get; init; } = "03:00";

    public void Validate()
    {
        if (Action is not ("viewer" or "host") || Mode is not ("interval" or "weekly") ||
            IntervalHours is < 1 or > 8760 || Days is null || Days.Length > 7 || Days.Any(day => day is < 0 or > 6) ||
            (Mode == "weekly" && Days.Length == 0) ||
            !TimeOnly.TryParseExact(Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("Choose a valid action, schedule, time, and at least one weekday. Intervals must be 1–8760 hours.");
    }

    public DateTimeOffset? NextAfter(DateTimeOffset now, TimeZoneInfo zone)
    {
        Validate();
        if (!Enabled) return null;
        if (Mode == "interval") return now.AddHours(IntervalHours);
        var time = TimeOnly.ParseExact(Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        for (var i = 0; i <= 7; i++)
        {
            var date = local.AddDays(i);
            if (!Days.Contains((int)date.DayOfWeek)) continue;
            var candidate = DateTime.SpecifyKind(date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
            // A nonexistent spring-forward time is skipped. Repeated fall-back times run only once.
            if (zone.IsInvalidTime(candidate)) continue;
            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(candidate, zone));
            if (utc > now) return utc;
        }
        // A weekly time skipped by daylight saving may next occur more than seven days away.
        return NextAfter(now.AddDays(1), zone);
    }
}
