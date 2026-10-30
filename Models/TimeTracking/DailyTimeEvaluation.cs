namespace ManufacturingTimeTracking.Models.TimeTracking;

/// <summary>
/// Snapshot of the end-of-day evaluation (colored icons) shown at clock-out, for later review in reports.
/// </summary>
public class DailyTimeEvaluation
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    /// <summary>UTC calendar date for the evaluation (midnight UTC, same convention as clock-out summary).</summary>
    public DateTime EvalDateUtc { get; set; }

    public decimal ScoreEntry { get; set; }
    public decimal ScoreExit { get; set; }
    public decimal ScoreTasks { get; set; }
    public decimal ScoreWorked { get; set; }
    public decimal ScoreBreak { get; set; }
    public decimal AverageScore { get; set; }

    public string EntryLabel { get; set; } = "";
    public string ExitLabel { get; set; } = "";
    public decimal TaskPercent { get; set; }
    public int WorkedSeconds { get; set; }
    public int BreakSeconds { get; set; }
    public int TaskMinutes { get; set; }
    public int MinutesInFactory { get; set; }

    /// <summary>Same HTML block shown in the clock-out modal.</summary>
    public string SummaryHtml { get; set; } = "";

    public DateTime RecordedAtUtc { get; set; }
}
