namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public enum SpcChartType { XBarR = 0, XBarS = 1, IndividualsMR = 2, P = 3, NP = 4, C = 5, U = 6 }

/// <summary>One plotted point with its control limits (attribute charts with varying n have per-point limits).</summary>
public sealed record SpcPoint(int Index, decimal Value, decimal Center, decimal Ucl, decimal Lcl, decimal? Range, List<string> Violations);
public sealed record SpcCapability(decimal Mean, decimal SigmaWithin, decimal SigmaOverall, decimal? Cp, decimal? Cpk, decimal? Pp, decimal? Ppk);
public sealed record SpcResult(SpcChartType Type, List<SpcPoint> Points, decimal Center, decimal Ucl, decimal Lcl, decimal? RangeCenter, decimal? RangeUcl, decimal? RangeLcl, SpcCapability? Capability, List<string> Violations);
/// <summary>A subgroup: measured values (variables charts) or defectives/defects with inspected units (attribute charts).</summary>
public sealed record SpcSubgroup(IReadOnlyList<decimal> Values, int? Count = null, int? Units = null);

/// <summary>
/// Control chart and capability math (AIAG SPC manual constants). Limits are computed from all subgroups supplied (phase I).
/// Rules (Western Electric / Nelson subset): 1 point beyond 3σ; 9 in a row on one side of center; 6 in a row steadily increasing or decreasing;
/// 2 of 3 beyond 2σ on the same side; 4 of 5 beyond 1σ on the same side.
/// </summary>
public static class SpcMath
{
    // index = subgroup size n (2..10)
    static readonly decimal[] A2 = [0, 0, 1.880m, 1.023m, 0.729m, 0.577m, 0.483m, 0.419m, 0.373m, 0.337m, 0.308m];
    static readonly decimal[] D3 = [0, 0, 0, 0, 0, 0, 0, 0.076m, 0.136m, 0.184m, 0.223m];
    static readonly decimal[] D4 = [0, 0, 3.267m, 2.574m, 2.282m, 2.114m, 2.004m, 1.924m, 1.864m, 1.816m, 1.777m];
    static readonly decimal[] D2 = [0, 0, 1.128m, 1.693m, 2.059m, 2.326m, 2.534m, 2.704m, 2.847m, 2.970m, 3.078m];
    static readonly decimal[] A3 = [0, 0, 2.659m, 1.954m, 1.628m, 1.427m, 1.287m, 1.182m, 1.099m, 1.032m, 0.975m];
    static readonly decimal[] B3 = [0, 0, 0, 0, 0, 0, 0.030m, 0.118m, 0.185m, 0.239m, 0.284m];
    static readonly decimal[] B4 = [0, 0, 3.267m, 2.568m, 2.266m, 2.089m, 1.970m, 1.882m, 1.815m, 1.761m, 1.716m];
    static readonly decimal[] C4 = [0, 0, 0.7979m, 0.8862m, 0.9213m, 0.9400m, 0.9515m, 0.9594m, 0.9650m, 0.9693m, 0.9727m];

    static decimal R(decimal v, int d = 4) => Math.Round(v, d, MidpointRounding.AwayFromZero);
    static decimal Sqrt(decimal v) => v <= 0 ? 0 : (decimal)Math.Sqrt((double)v);
    static decimal StdDev(IReadOnlyList<decimal> v) { if (v.Count < 2) return 0; var m = v.Average(); return Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1)); }

    public static SpcResult Compute(SpcChartType type, IReadOnlyList<SpcSubgroup> groups, decimal? lsl, decimal? usl)
    {
        if (groups.Count == 0) throw new ArgumentException("No samples.");
        return type switch
        {
            SpcChartType.XBarR or SpcChartType.XBarS => XBar(type, groups, lsl, usl),
            SpcChartType.IndividualsMR => Individuals(groups, lsl, usl),
            _ => Attribute(type, groups),
        };
    }

    static SpcResult XBar(SpcChartType type, IReadOnlyList<SpcSubgroup> groups, decimal? lsl, decimal? usl)
    {
        var n = groups[0].Values.Count;
        if (n is < 2 or > 10 || groups.Any(g => g.Values.Count != n)) throw new ArgumentException("X-bar charts need subgroups of the same size between 2 and 10.");
        var means = groups.Select(g => g.Values.Average()).ToList();
        var xbb = means.Average();
        decimal center2, ucl2, lcl2, ucl, lcl, sigma;
        List<decimal> disp;
        if (type == SpcChartType.XBarR)
        {
            disp = groups.Select(g => g.Values.Max() - g.Values.Min()).ToList();
            center2 = disp.Average(); ucl = xbb + A2[n] * center2; lcl = xbb - A2[n] * center2; ucl2 = D4[n] * center2; lcl2 = D3[n] * center2; sigma = center2 / D2[n];
        }
        else
        {
            disp = groups.Select(g => StdDev(g.Values)).ToList();
            center2 = disp.Average(); ucl = xbb + A3[n] * center2; lcl = xbb - A3[n] * center2; ucl2 = B4[n] * center2; lcl2 = B3[n] * center2; sigma = center2 / C4[n];
        }
        var pts = means.Select((v, i) => new SpcPoint(i + 1, R(v), R(xbb), R(ucl), R(lcl), R(disp[i]), [])).ToList();
        var viol = Rules(pts, xbb, (ucl - xbb) / 3);
        for (var i = 0; i < disp.Count; i++) if (disp[i] > ucl2 || disp[i] < lcl2) { pts[i].Violations.Add("Range/sigma beyond control limit"); viol.Add($"Point {i + 1}: dispersion beyond control limit"); }
        return new(type, pts, R(xbb), R(ucl), R(lcl), R(center2), R(ucl2), R(lcl2), Capability(groups.SelectMany(g => g.Values).ToList(), sigma, lsl, usl), viol);
    }

    static SpcResult Individuals(IReadOnlyList<SpcSubgroup> groups, decimal? lsl, decimal? usl)
    {
        var x = groups.Select(g => g.Values.Count == 1 ? g.Values[0] : throw new ArgumentException("Individuals charts take one value per sample.")).ToList();
        if (x.Count < 2) throw new ArgumentException("Individuals charts need at least two samples.");
        var mr = x.Skip(1).Select((v, i) => Math.Abs(v - x[i])).ToList();
        var mrbar = mr.Average(); var xbar = x.Average();
        var ucl = xbar + 2.66m * mrbar; var lcl = xbar - 2.66m * mrbar;
        var pts = x.Select((v, i) => new SpcPoint(i + 1, R(v), R(xbar), R(ucl), R(lcl), i == 0 ? null : R(mr[i - 1]), [])).ToList();
        var viol = Rules(pts, xbar, (ucl - xbar) / 3);
        for (var i = 0; i < mr.Count; i++) if (mr[i] > 3.267m * mrbar) { pts[i + 1].Violations.Add("Moving range beyond control limit"); viol.Add($"Point {i + 2}: moving range beyond control limit"); }
        return new(SpcChartType.IndividualsMR, pts, R(xbar), R(ucl), R(lcl), R(mrbar), R(3.267m * mrbar), 0, Capability(x, mrbar / 1.128m, lsl, usl), viol);
    }

    static SpcResult Attribute(SpcChartType type, IReadOnlyList<SpcSubgroup> groups)
    {
        if (groups.Any(g => g.Count is null || g.Count < 0)) throw new ArgumentException("Attribute charts need a count for every sample.");
        if (type is SpcChartType.P or SpcChartType.NP or SpcChartType.U && groups.Any(g => g.Units is null or < 1)) throw new ArgumentException("This chart needs the number of units inspected for every sample.");
        if (type is SpcChartType.P or SpcChartType.NP && groups.Any(g => g.Count > g.Units)) throw new ArgumentException("Defectives cannot exceed units inspected.");
        decimal center; Func<SpcSubgroup, (decimal V, decimal U, decimal L)> f;
        switch (type)
        {
            case SpcChartType.P:
                center = (decimal)groups.Sum(g => g.Count!.Value) / groups.Sum(g => g.Units!.Value);
                f = g => { var s = 3 * Sqrt(center * (1 - center) / g.Units!.Value); return ((decimal)g.Count!.Value / g.Units!.Value, center + s, Math.Max(0, center - s)); };
                break;
            case SpcChartType.NP:
                if (groups.Select(g => g.Units).Distinct().Count() > 1) throw new ArgumentException("np charts need a constant sample size; use a p chart.");
                var nn = groups[0].Units!.Value; var pbar = (decimal)groups.Sum(g => g.Count!.Value) / (nn * groups.Count);
                center = nn * pbar; var snp = 3 * Sqrt(nn * pbar * (1 - pbar));
                f = g => (g.Count!.Value, center + snp, Math.Max(0, center - snp));
                break;
            case SpcChartType.C:
                center = (decimal)groups.Average(g => g.Count!.Value);
                f = g => (g.Count!.Value, center + 3 * Sqrt(center), Math.Max(0, center - 3 * Sqrt(center)));
                break;
            default:
                center = (decimal)groups.Sum(g => g.Count!.Value) / groups.Sum(g => g.Units!.Value);
                f = g => { var s = 3 * Sqrt(center / g.Units!.Value); return ((decimal)g.Count!.Value / g.Units!.Value, center + s, Math.Max(0, center - s)); };
                break;
        }
        var pts = groups.Select((g, i) => { var (v, u, l) = f(g); return new SpcPoint(i + 1, R(v), R(center), R(u), R(l), null, []); }).ToList();
        var viol = new List<string>();
        foreach (var p in pts) if (p.Value > p.Ucl || p.Value < p.Lcl) { p.Violations.Add("Beyond 3σ"); viol.Add($"Point {p.Index}: beyond control limits"); }
        Run(pts, center, viol, 9, "9 in a row on one side of the center line");
        Trend(pts, viol);
        return new(type, pts, R(center), pts.Max(p => p.Ucl), pts.Min(p => p.Lcl), null, null, null, null, viol);
    }

    static List<string> Rules(List<SpcPoint> pts, decimal center, decimal sigma)
    {
        var viol = new List<string>();
        foreach (var p in pts) if (p.Value > p.Ucl || p.Value < p.Lcl) { p.Violations.Add("Beyond 3σ"); viol.Add($"Point {p.Index}: beyond control limits"); }
        Run(pts, center, viol, 9, "9 in a row on one side of the center line");
        Trend(pts, viol);
        if (sigma > 0)
        {
            Zone(pts, center, sigma, 2, 3, 2, "2 of 3 beyond 2σ", viol);
            Zone(pts, center, sigma, 1, 5, 4, "4 of 5 beyond 1σ", viol);
        }
        return viol;
    }

    static void Run(List<SpcPoint> pts, decimal center, List<string> viol, int len, string label)
    {
        for (var i = len - 1; i < pts.Count; i++)
        {
            var w = pts.Skip(i - len + 1).Take(len).ToList();
            if (w.All(p => p.Value > center) || w.All(p => p.Value < center)) { pts[i].Violations.Add(label); viol.Add($"Point {pts[i].Index}: {label}"); }
        }
    }
    static void Trend(List<SpcPoint> pts, List<string> viol)
    {
        for (var i = 5; i < pts.Count; i++)
        {
            var w = pts.Skip(i - 5).Take(6).Select(p => p.Value).ToList();
            var up = w.Zip(w.Skip(1)).All(t => t.Second > t.First); var down = w.Zip(w.Skip(1)).All(t => t.Second < t.First);
            if (up || down) { pts[i].Violations.Add("Trend of 6"); viol.Add($"Point {pts[i].Index}: 6 in a row {(up ? "increasing" : "decreasing")}"); }
        }
    }
    static void Zone(List<SpcPoint> pts, decimal center, decimal sigma, int k, int window, int need, string label, List<string> viol)
    {
        for (var i = window - 1; i < pts.Count; i++)
        {
            var w = pts.Skip(i - window + 1).Take(window).ToList();
            if (w.Count(p => p.Value > center + k * sigma) >= need && pts[i].Value > center + k * sigma || w.Count(p => p.Value < center - k * sigma) >= need && pts[i].Value < center - k * sigma)
            { pts[i].Violations.Add(label); viol.Add($"Point {pts[i].Index}: {label}"); }
        }
    }

    /// <summary>Cp/Cpk use within-subgroup sigma (R̄/d2, s̄/c4 or MR̄/1.128); Pp/Ppk use the overall sample standard deviation. One-sided specs give only the k index.</summary>
    public static SpcCapability Capability(IReadOnlyList<decimal> all, decimal sigmaWithin, decimal? lsl, decimal? usl)
    {
        var mean = all.Average(); var so = StdDev(all);
        decimal? Idx(decimal s, bool k) => s <= 0 ? null : k
            ? new[] { usl is { } u ? (u - mean) / (3 * s) : (decimal?)null, lsl is { } l ? (mean - l) / (3 * s) : null }.Where(x => x is not null).Min()
            : usl is { } uu && lsl is { } ll ? (uu - ll) / (6 * s) : null;
        decimal? Rd(decimal? v) => v is null ? null : R(v.Value, 3);
        return new(R(mean), R(sigmaWithin), R(so), Rd(Idx(sigmaWithin, false)), Rd(Idx(sigmaWithin, true)), Rd(Idx(so, false)), Rd(Idx(so, true)));
    }
}
