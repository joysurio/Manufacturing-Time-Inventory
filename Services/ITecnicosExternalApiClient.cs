namespace ManufacturingTimeTracking.Services;

/// <summary>Calls the external Tecnicos IsActive API (Websat) before clock-out.</summary>
public interface ITecnicosExternalApiClient
{
    /// <summary>
    /// <c>true</c> if the user is active in the other app; <c>false</c> if not active or response says inactive;
    /// <c>null</c> if the API is not configured or the request failed (caller should fail open).
    /// </summary>
    Task<bool?> TryGetIsActiveAsync(string user, CancellationToken cancellationToken = default);
}
