namespace ManufacturingTimeTracking.Models.TimeTracking;

/// <summary>
/// One time tracking event: CLOCK_IN, CLOCK_OUT, BREAK_START, or BREAK_END.
/// SessionId groups events from one work session (clock-in to clock-out).
/// </summary>
public class TimeTrackingEntry
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    /// <summary>CLOCK_IN | CLOCK_OUT | BREAK_START | BREAK_END</summary>
    public string ActionType { get; set; } = "";
    public int? BreakReasonId { get; set; }
    public DateTime TimestampUtc { get; set; }
    /// <summary>Same value for all events in one work session (from clock-in to clock-out).</summary>
    public Guid? SessionId { get; set; }

    public BreakReason? BreakReason { get; set; }
}
