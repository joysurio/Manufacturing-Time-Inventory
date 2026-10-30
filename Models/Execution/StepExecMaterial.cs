using System.Globalization;

namespace ManufacturingTimeTracking.Models.Execution;

public class StepExecMaterial
{
    public int Id { get; set; }
    public int StepExecId { get; set; }
    public string MaterialName { get; set; } = string.Empty; // Snapshot as text
    public decimal? Qty { get; set; }
    public string? Unit { get; set; }
    
    // Navigation
    public StepExec StepExec { get; set; } = null!;

    /// <summary>Operator step UI: name only when unit is currency (issue #52 — no prices on step screen).</summary>
    public string ToOperatorDisplayString() =>
        FormatMaterialLineForOperator(MaterialName, Qty, Unit);

    /// <summary>True when the catalog/material unit indicates money, not a physical measure.</summary>
    public static bool IsCurrencyUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        if (u.Contains('€', StringComparison.Ordinal) || u.Contains('$') || u.Contains('£') || u.Contains('¥'))
            return true;
        if (u.Length <= 4 && u.All(c => char.IsLetter(c) || c == '€'))
        {
            var code = new string(u.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (code is "EUR" or "USD" or "GBP" or "MXN" or "COP" or "CHF" or "JPY" or "CNY" or "AUD" or "CAD" or "BRL" or "ARS" or "PEN" or "CLP" or "DOP")
                return true;
        }
        return false;
    }

    public static string FormatMaterialLineForOperator(string materialName, decimal? qty, string? unit)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return "";
        if (IsCurrencyUnit(unit))
            return materialName.Trim();
        if (!qty.HasValue && string.IsNullOrWhiteSpace(unit))
            return materialName.Trim();
        var qtyPart = qty.HasValue
            ? $" - {qty.Value.ToString("0.####", CultureInfo.InvariantCulture)}"
            : "";
        var unitPart = !string.IsNullOrWhiteSpace(unit) ? $" {unit.Trim()}" : "";
        return materialName.Trim() + qtyPart + unitPart;
    }
}
