namespace ManufacturingTimeTracking.Models.TimeTracking;

/// <summary>
/// Configurable reason for a break (e.g. Lunch, Coffee break, Meeting).
/// Used when recording BREAK_START in time tracking.
/// </summary>
public class BreakReason
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Active { get; set; } = true;
}
