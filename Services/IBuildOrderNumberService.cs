namespace ManufacturingTimeTracking.Services;

public interface IBuildOrderNumberService
{
    /// <summary>Generates the next unique order number and matching serial for the current year.</summary>
    Task<(string ExternalRef, string SerialNumber)> GenerateNextAsync(CancellationToken cancellationToken = default);
}
