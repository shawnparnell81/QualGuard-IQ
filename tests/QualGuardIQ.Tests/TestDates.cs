namespace QualGuardIQ.Tests;

/// <summary>Business "today" for test sites, which use the default America/New_York time zone.</summary>
public static class TestDates
{
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
}
