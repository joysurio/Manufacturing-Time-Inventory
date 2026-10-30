using ManufacturingTimeTracking.Data;
using Microsoft.EntityFrameworkCore;

namespace ManufacturingTimeTracking.Services;

public class BuildOrderNumberService : IBuildOrderNumberService
{
    private readonly ApplicationDbContext _context;

    public BuildOrderNumberService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<(string ExternalRef, string SerialNumber)> GenerateNextAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var ordPrefix = $"ORD-{year}-";
        var snPrefix = $"SN-{year}-";

        var existingRefs = await _context.BuildOrders
            .AsNoTracking()
            .Where(o => o.ExternalRef.StartsWith(ordPrefix))
            .Select(o => o.ExternalRef)
            .ToListAsync(cancellationToken);

        var maxSeq = 0;
        foreach (var reference in existingRefs)
        {
            var suffix = reference.Length > ordPrefix.Length ? reference[ordPrefix.Length..] : "";
            if (int.TryParse(suffix, out var seq))
                maxSeq = Math.Max(maxSeq, seq);
        }

        var next = maxSeq + 1;
        return ($"{ordPrefix}{next:D5}", $"{snPrefix}{next:D5}");
    }
}
