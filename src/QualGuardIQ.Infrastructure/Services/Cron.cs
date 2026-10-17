namespace QualGuardIQ.Infrastructure.Services;

/// <summary>Five-field cron (minute hour day-of-month month day-of-week) in UTC: *, numbers, lists, ranges and steps.</summary>
public sealed class CronSchedule
{
    private readonly bool[] _min = new bool[60], _hour = new bool[24], _dom = new bool[32], _mon = new bool[13], _dow = new bool[7];
    private readonly bool _domAny, _dowAny;

    private CronSchedule(string expr)
    {
        var f = expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5) throw new FormatException("A schedule needs five fields: minute hour day month weekday.");
        Fill(_min, f[0], 0, 59); Fill(_hour, f[1], 0, 23); Fill(_dom, f[2], 1, 31); Fill(_mon, f[3], 1, 12);
        var dow = new bool[8]; Fill(dow, f[4], 0, 7); for (var i = 0; i < 7; i++) _dow[i] = dow[i] || (i == 0 && dow[7]);
        _domAny = f[2] == "*"; _dowAny = f[4] == "*";
    }

    public static CronSchedule Parse(string expr) => new(expr);

    private static void Fill(bool[] set, string field, int lo, int hi)
    {
        foreach (var part in field.Split(','))
        {
            var step = 1; var range = part;
            if (part.Contains('/')) { var s = part.Split('/'); range = s[0]; step = int.Parse(s[1]); if (step < 1) throw new FormatException("Bad step."); }
            int a, b;
            if (range == "*") { a = lo; b = hi; }
            else if (range.Contains('-')) { var r = range.Split('-'); a = int.Parse(r[0]); b = int.Parse(r[1]); }
            else { a = int.Parse(range); b = part.Contains('/') ? hi : a; }
            if (a < lo || b > hi || a > b) throw new FormatException($"Value out of range in '{field}'.");
            for (var v = a; v <= b; v += step) set[v] = true;
        }
    }

    public DateTimeOffset? Next(DateTimeOffset from)
    {
        var t = new DateTime(from.UtcDateTime.Year, from.UtcDateTime.Month, from.UtcDateTime.Day, from.UtcDateTime.Hour, from.UtcDateTime.Minute, 0, DateTimeKind.Utc).AddMinutes(1);
        for (var i = 0; i < 60 * 24 * 366 * 2; i++, t = t.AddMinutes(1))
        {
            if (!_mon[t.Month] || !_hour[t.Hour] || !_min[t.Minute]) continue;
            bool dom = _dom[t.Day], dow = _dow[(int)t.DayOfWeek];
            var dayOk = _domAny && _dowAny ? true : _domAny ? dow : _dowAny ? dom : dom || dow;
            if (dayOk) return new DateTimeOffset(t, TimeSpan.Zero);
        }
        return null;
    }
}
