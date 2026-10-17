using QualGuardIQ.Domain.Quality;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

/// <summary>
/// RPN = S × O × D. Action priority follows the AIAG-VDA FMEA Handbook (2019), "Table AP – Action Priority for
/// DFMEA and PFMEA", looked up by severity band, occurrence band and detection band. Checked against the published
/// table for all 1000 combinations (ActionPriorityTableTests). Verified 2026-10-17 against two published reproductions of the
/// handbook table (Relyence FMEA AP reference; an independent transcription citing handbook pp. 70-71 and 116-117): all 25
/// severity/occurrence rows agree. The FMEA-MSR table is different and is not used here.
/// </summary>
public static class PfmeaMath
{
    public static int Rpn(int s, int o, int d) => s * o * d;

    public static void Validate(int s, int o, int d)
    {
        foreach (var (v, n) in new[] { (s, "Severity"), (o, "Occurrence"), (d, "Detection") })
            if (v is < 1 or > 10) throw new QualGuardIQ.Application.Abstractions.AppException($"{n} must be between 1 and 10.");
    }

    public static ActionPriority Priority(int s, int o, int d)
    {
        // detection band index: 0 = 7-10, 1 = 5-6, 2 = 2-4, 3 = 1
        int db = d >= 7 ? 0 : d >= 5 ? 1 : d >= 2 ? 2 : 3;
        const ActionPriority H = ActionPriority.High, M = ActionPriority.Medium, L = ActionPriority.Low;
        ActionPriority[] row;
        if (s >= 9)
            row = o >= 8 ? [H, H, H, H] : o >= 6 ? [H, H, H, H] : o >= 4 ? [H, H, H, M] : o >= 2 ? [H, M, L, L] : [L, L, L, L];
        else if (s >= 7)
            row = o >= 8 ? [H, H, H, H] : o >= 6 ? [H, H, H, M] : o >= 4 ? [H, M, M, M] : o >= 2 ? [M, M, L, L] : [L, L, L, L];
        else if (s >= 4)
            row = o >= 8 ? [H, H, M, M] : o >= 6 ? [M, M, M, L] : o >= 4 ? [M, L, L, L] : [L, L, L, L];
        else if (s >= 2)
            row = o >= 8 ? [M, M, L, L] : [L, L, L, L];
        else row = [L, L, L, L];
        return row[db];
    }
}
