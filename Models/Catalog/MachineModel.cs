using System.ComponentModel.DataAnnotations;

namespace ManufacturingTimeTracking.Models.Catalog;

public class MachineModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;

    /// <summary>Standard hours per unit for workload-based planning (issue #11). Required, must be > 0.</summary>
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Standard hours must be greater than 0.")]
    public decimal StandardHours { get; set; } = 1;

    // Navigation
    public ICollection<MachineVariant> Variants { get; set; } = new List<MachineVariant>();
    /// <summary>Components (spare parts) required per machine for pick lists.</summary>
    public ICollection<MachineModelComponent> Components { get; set; } = new List<MachineModelComponent>();
}
