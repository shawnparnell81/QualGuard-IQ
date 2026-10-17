using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Modules.Apqp;

namespace QualGuardIQ.Tests;

/// <summary>
/// Checks every one of the 1000 S/O/D combinations against the published AIAG-VDA FMEA Handbook (2019)
/// "Table AP – Action Priority for DFMEA and PFMEA", transcribed here independently of PfmeaMath.
/// Rows: severity band, occurrence band. Columns: detection 7-10, 5-6, 2-4, 1.
/// </summary>
public sealed class ActionPriorityTableTests
{
    private static readonly Dictionary<(string S, string O), string> Published = new()
    {
        [("9-10", "8-10")] = "HHHH", [("9-10", "6-7")] = "HHHH", [("9-10", "4-5")] = "HHHM", [("9-10", "2-3")] = "HMLL", [("9-10", "1")] = "LLLL",
        [("7-8", "8-10")] = "HHHH", [("7-8", "6-7")] = "HHHM", [("7-8", "4-5")] = "HMMM", [("7-8", "2-3")] = "MMLL", [("7-8", "1")] = "LLLL",
        [("4-6", "8-10")] = "HHMM", [("4-6", "6-7")] = "MMML", [("4-6", "4-5")] = "MLLL", [("4-6", "2-3")] = "LLLL", [("4-6", "1")] = "LLLL",
        [("2-3", "8-10")] = "MMLL", [("2-3", "6-7")] = "LLLL", [("2-3", "4-5")] = "LLLL", [("2-3", "2-3")] = "LLLL", [("2-3", "1")] = "LLLL",
        [("1", "8-10")] = "LLLL", [("1", "6-7")] = "LLLL", [("1", "4-5")] = "LLLL", [("1", "2-3")] = "LLLL", [("1", "1")] = "LLLL",
    };

    private static string SBand(int s) => s >= 9 ? "9-10" : s >= 7 ? "7-8" : s >= 4 ? "4-6" : s >= 2 ? "2-3" : "1";
    private static string OBand(int o) => o >= 8 ? "8-10" : o >= 6 ? "6-7" : o >= 4 ? "4-5" : o >= 2 ? "2-3" : "1";
    private static int DCol(int d) => d >= 7 ? 0 : d >= 5 ? 1 : d >= 2 ? 2 : 3;

    [Fact]
    public void All_1000_combinations_match_the_published_table()
    {
        var mismatches = new List<string>();
        for (var s = 1; s <= 10; s++)
        for (var o = 1; o <= 10; o++)
        for (var d = 1; d <= 10; d++)
        {
            var expected = Published[(SBand(s), OBand(o))][DCol(d)] switch { 'H' => ActionPriority.High, 'M' => ActionPriority.Medium, _ => ActionPriority.Low };
            var actual = PfmeaMath.Priority(s, o, d);
            if (actual != expected) mismatches.Add($"S{s} O{o} D{d}: expected {expected}, got {actual}");
        }
        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData(10, 1, 10, ActionPriority.Low)]   // occurrence 1 is always Low
    [InlineData(9, 2, 10, ActionPriority.High)]
    [InlineData(9, 2, 5, ActionPriority.Medium)]
    [InlineData(9, 4, 1, ActionPriority.Medium)]
    [InlineData(8, 6, 1, ActionPriority.Medium)]
    [InlineData(5, 8, 4, ActionPriority.Medium)]
    [InlineData(6, 6, 1, ActionPriority.Low)]
    [InlineData(3, 8, 6, ActionPriority.Medium)]
    [InlineData(3, 7, 10, ActionPriority.Low)]
    public void Band_edges(int s, int o, int d, ActionPriority expected) => Assert.Equal(expected, PfmeaMath.Priority(s, o, d));
}
