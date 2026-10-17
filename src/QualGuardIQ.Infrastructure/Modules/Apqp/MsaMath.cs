namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public sealed record GrrMeasurement(string Operator, int Part, int Trial, decimal Value);
public sealed record GrrResult(int Operators, int Parts, int Trials, decimal Repeatability, decimal Reproducibility, decimal Grr, decimal PartVariation, decimal TotalVariation,
    decimal PctEv, decimal PctAv, decimal PctGrr, decimal PctPv, decimal PctContributionGrr, decimal? PctTolerance, int Ndc, string Verdict);
public sealed record AttributeMeasurement(string Operator, int Part, int Trial, string Value, string? Reference);
public sealed record AttributeResult(decimal WithinAppraiserPct, decimal VsReferencePct, decimal Kappa, List<AppraiserAgreement> Appraisers, string Verdict);
public sealed record AppraiserAgreement(string Operator, decimal WithinPct, decimal VsReferencePct);
/// <summary>
/// Bias study result (one-sample Student t-test of the readings against the reference value, two-sided).
/// T is null when the readings have no spread (the statistic is undefined); Significant then means "any non-zero bias".
/// </summary>
public sealed record BiasResult(int N, decimal Mean, decimal Reference, decimal Bias, decimal StdDev, decimal? T, decimal? PctTolerance, bool Significant,
    int DegreesOfFreedom = 0, decimal Confidence = 0.95m, decimal? StdError = null, decimal? CriticalT = null, decimal? PValue = null, decimal? BiasLow = null, decimal? BiasHigh = null,
    string? Note = null, string CalculationVersion = MsaMath.BiasCalculationVersion);
public sealed record LinearityResult(decimal Slope, decimal Intercept, decimal RSquared, decimal PctLinearity, List<(decimal Reference, decimal MeanBias)> Points);

/// <summary>
/// Measurement system analysis (AIAG MSA 4th edition, average-and-range method for variable gage R&amp;R).
/// EV = R̿·K1; AV = √((X̄diff·K2)² − EV²/(n·r)); GRR = √(EV²+AV²); PV = Rp·K3; TV = √(GRR²+PV²); ndc = 1.41·PV/GRR.
/// Verdict on %GRR (of study variation): &lt;10% acceptable, 10–30% marginal, &gt;30% unacceptable.
/// </summary>
public static class MsaMath
{
    static readonly Dictionary<int, decimal> K1 = new() { [2] = 0.8862m, [3] = 0.5908m };
    static readonly Dictionary<int, decimal> K2 = new() { [2] = 0.7071m, [3] = 0.5231m };
    static readonly Dictionary<int, decimal> K3 = new() { [2] = 0.7071m, [3] = 0.5231m, [4] = 0.4467m, [5] = 0.4030m, [6] = 0.3742m, [7] = 0.3534m, [8] = 0.3375m, [9] = 0.3249m, [10] = 0.3146m };
    static decimal Sqrt(decimal v) => v <= 0 ? 0 : (decimal)Math.Sqrt((double)v);
    static decimal R(decimal v, int d = 4) => Math.Round(v, d, MidpointRounding.AwayFromZero);
    static decimal Pct(decimal a, decimal b) => b == 0 ? 0 : R(100 * a / b, 2);

    public static GrrResult VariableGrr(IReadOnlyList<GrrMeasurement> data, decimal? tolerance)
    {
        var ops = data.Select(d => d.Operator).Distinct().ToList();
        var parts = data.Select(d => d.Part).Distinct().OrderBy(x => x).ToList();
        var trials = data.Select(d => d.Trial).Distinct().ToList();
        if (!K2.ContainsKey(ops.Count)) throw new ArgumentException("Variable gage R&R needs 2 or 3 operators.");
        if (!K1.ContainsKey(trials.Count)) throw new ArgumentException("Variable gage R&R needs 2 or 3 trials.");
        if (!K3.ContainsKey(parts.Count)) throw new ArgumentException("Variable gage R&R needs 2 to 10 parts.");
        if (data.Count != ops.Count * parts.Count * trials.Count || data.GroupBy(d => (d.Operator, d.Part, d.Trial)).Any(g => g.Count() > 1))
            throw new ArgumentException("Every operator must measure every part once per trial.");
        int n = parts.Count, r = trials.Count;
        var rbar = ops.Select(o => parts.Select(p => { var v = data.Where(d => d.Operator == o && d.Part == p).Select(d => d.Value).ToList(); return v.Max() - v.Min(); }).Average()).Average();
        var opAvg = ops.Select(o => data.Where(d => d.Operator == o).Average(d => d.Value)).ToList();
        var xdiff = opAvg.Max() - opAvg.Min();
        var partAvg = parts.Select(p => data.Where(d => d.Part == p).Average(d => d.Value)).ToList();
        var rp = partAvg.Max() - partAvg.Min();
        var ev = rbar * K1[r];
        var av = Sqrt((xdiff * K2[ops.Count]) * (xdiff * K2[ops.Count]) - ev * ev / (n * r));
        var grr = Sqrt(ev * ev + av * av);
        var pv = rp * K3[n];
        var tv = Sqrt(grr * grr + pv * pv);
        var pctGrr = Pct(grr, tv);
        var ndc = grr == 0 ? 99 : (int)Math.Floor(1.41m * pv / grr);
        return new(ops.Count, n, r, R(ev), R(av), R(grr), R(pv), R(tv), Pct(ev, tv), Pct(av, tv), pctGrr, Pct(pv, tv), tv == 0 ? 0 : R(100 * grr * grr / (tv * tv), 2),
            tolerance is > 0 ? Pct(6 * grr, tolerance.Value) : null, ndc,
            pctGrr < 10 ? "Acceptable" : pctGrr <= 30 ? "Marginal" : "Unacceptable");
    }

    /// <summary>Attribute agreement: within-appraiser repeatability, agreement with the reference, and pooled Cohen's kappa vs reference.</summary>
    public static AttributeResult Attribute(IReadOnlyList<AttributeMeasurement> data)
    {
        if (data.Count == 0) throw new ArgumentException("No results.");
        if (data.Any(d => string.IsNullOrWhiteSpace(d.Reference))) throw new ArgumentException("Attribute studies need the reference decision for every part.");
        var ops = data.Select(d => d.Operator).Distinct().ToList();
        var app = ops.Select(o =>
        {
            var mine = data.Where(d => d.Operator == o).GroupBy(d => d.Part).ToList();
            var within = mine.Count(g => g.Select(x => x.Value).Distinct().Count() == 1);
            var vsRef = mine.Count(g => g.All(x => x.Value == x.Reference));
            return new AppraiserAgreement(o, Pct(within, mine.Count), Pct(vsRef, mine.Count));
        }).ToList();
        var po = (decimal)data.Count(d => d.Value == d.Reference) / data.Count;
        var cats = data.Select(d => d.Value).Concat(data.Select(d => d.Reference!)).Distinct();
        var pe = cats.Sum(c => (decimal)data.Count(d => d.Value == c) / data.Count * data.Count(d => d.Reference == c) / data.Count);
        var kappa = pe == 1 ? 1 : R((po - pe) / (1 - pe), 3);
        var vsRefAll = app.Average(a => a.VsReferencePct);
        return new(R(app.Average(a => a.WithinPct), 2), R(vsRefAll, 2), kappa, app, kappa >= 0.9m ? "Acceptable" : kappa >= 0.75m ? "Marginal" : "Unacceptable");
    }

    /// <summary>Current bias calculation: exact Student t quantile and p-value (MathNet.Numerics), n − 1 degrees of freedom, configurable confidence.</summary>
    public const string BiasCalculationVersion = "bias-t/2 (Student t, MathNet.Numerics)";
    /// <summary>Label for results computed before version 2 (critical t taken from a five-step approximation, not the study's degrees of freedom).</summary>
    public const string LegacyCalculationVersion = "msa/1 (approximate critical t)";

    public static BiasResult Bias(IReadOnlyList<decimal> values, decimal reference, decimal? tolerance, decimal confidence = 0.95m)
    {
        if (confidence is < 0.80m or > 0.999m) throw new ArgumentException("Confidence must be between 80% and 99.9%.");
        if (values.Count < 2) throw new ArgumentException("A bias study needs at least 2 readings (10+ recommended).");
        var n = values.Count; var dof = n - 1;
        var mean = values.Average(); var sd = Sqrt(values.Sum(x => (x - mean) * (x - mean)) / dof);
        var bias = mean - reference;
        decimal? pct = tolerance is > 0 ? Pct(Math.Abs(bias), tolerance.Value) : null;
        var tDist = new MathNet.Numerics.Distributions.StudentT(0, 1, dof);
        var crit = tDist.InverseCumulativeDistribution(1 - (1 - (double)confidence) / 2);
        if (sd == 0)
        {
            // No spread: the t statistic is undefined. Any difference from the reference is then a real bias; none means none.
            return new(n, R(mean), reference, R(bias), 0, null, pct, bias != 0, dof, confidence, 0, R((decimal)crit, 4), bias == 0 ? 1m : 0m, R(bias), R(bias),
                bias == 0 ? "All readings equal the reference value." : "All readings are identical, so the t statistic is undefined; the non-zero bias is treated as significant.");
        }
        var se = sd / Sqrt(n);
        var t = (double)(bias / se);
        var p = 2 * (1 - tDist.CumulativeDistribution(Math.Abs(t)));
        var half = (decimal)crit * se;
        return new(n, R(mean), reference, R(bias), R(sd), R((decimal)t, 4), pct, Math.Abs(t) > crit, dof, confidence, R(se), R((decimal)crit, 4), R((decimal)p, 6), R(bias - half), R(bias + half),
            n < 10 ? "Fewer than 10 readings; AIAG MSA recommends at least 10." : null);
    }

    /// <summary>Least-squares line of bias against reference value; %linearity = 100·|slope|.</summary>
    public static LinearityResult Linearity(IReadOnlyList<(decimal Reference, decimal Value)> data)
    {
        if (data.Select(d => d.Reference).Distinct().Count() < 2) throw new ArgumentException("A linearity study needs readings at 2 or more reference values (5 recommended).");
        var x = data.Select(d => d.Reference).ToList(); var y = data.Select(d => d.Value - d.Reference).ToList();
        var mx = x.Average(); var my = y.Average();
        var sxx = x.Sum(v => (v - mx) * (v - mx)); var sxy = x.Zip(y).Sum(t => (t.First - mx) * (t.Second - my)); var syy = y.Sum(v => (v - my) * (v - my));
        var slope = sxx == 0 ? 0 : sxy / sxx; var icpt = my - slope * mx;
        var r2 = sxx == 0 || syy == 0 ? 0 : sxy * sxy / (sxx * syy);
        var pts = data.GroupBy(d => d.Reference).OrderBy(g => g.Key).Select(g => (g.Key, R(g.Average(d => d.Value - d.Reference)))).ToList();
        return new(R(slope, 5), R(icpt, 5), R(r2, 4), R(100 * Math.Abs(slope), 2), pts);
    }
}
