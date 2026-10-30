using System.ComponentModel.DataAnnotations;
using ManufacturingTimeTracking.Models.Catalog;

namespace ManufacturingTimeTracking.Models.Execution;

/// <summary>Order priority: 1=Low, 2=Normal, 3=High, 4=Urgent.</summary>
public static class BuildOrderPriority
{
    public const int Low = 1;
    public const int Normal = 2;
    public const int High = 3;
    public const int Urgent = 4;

    public static string DisplayName(int priority)
    {
        return priority switch
        {
            Low => "Low",
            Normal => "Normal",
            High => "High",
            Urgent => "Urgent",
            _ => "Normal"
        };
    }
}

public class BuildOrder
{
    public int Id { get; set; }
    public string ExternalRef { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public int MachineModelId { get; set; }
    public int MachineVariantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, Cancelled

    // Planning / ownership (issue #8, #12)
    public DateTime? DueDate { get; set; }
    public int Priority { get; set; } = BuildOrderPriority.Normal;
    /// <summary>Planned week (e.g. 2024-W12) for workload planning. Required.</summary>
    [Required(ErrorMessage = "Planned week is required.")]
    [MaxLength(20)]
    public string PlannedWeek { get; set; } = string.Empty;

    [Required(ErrorMessage = "Customer name is required.")]
    public string CustomerName { get; set; } = string.Empty;

    // Optional: customer identifier (e.g. client internal id)
    public string? CustomerIdentifier { get; set; }

    // Manufacturing plant or site name
    public string? PlantName { get; set; }

    // Physical location or bay for this order
    public string? Location { get; set; }

    public string? ProjectRef { get; set; }

    /// <summary>Accountable person for the order (AspNetUsers.Id).</summary>
    [Required(ErrorMessage = "Owner is required.")]
    public string OwnerUserId { get; set; } = string.Empty;

    // Blocked state (issue #9)
    public bool IsBlocked { get; set; }
    public int? BlockedReasonId { get; set; }
    public string? BlockedNotes { get; set; }
    public DateTime? BlockedAt { get; set; }

    // Navigation
    public BlockedReason? BlockedReason { get; set; }
    public ICollection<BuildExecution> Executions { get; set; } = new List<BuildExecution>();
    public ICollection<ProductionInterruption> Interruptions { get; set; } = new List<ProductionInterruption>();
}
