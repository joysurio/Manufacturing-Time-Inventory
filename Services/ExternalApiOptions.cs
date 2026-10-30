namespace ManufacturingTimeTracking.Services;

public class ExternalApiOptions
{
    public const string SectionName = "ExternalApi";

    /// <summary>Base URL of the Websat / Tecnicos API (no trailing slash). Example: https://lafabricadesoftware.dyndns.org</summary>
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Request timeout in seconds. Default 30.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// When true, TLS server certificate validation is skipped (same idea as curl -k).
    /// Use only if the API presents a cert that does not match the URL (e.g. HTTPS to an IP).
    /// Prefer fixing DNS and using <see cref="BaseUrl"/> with the certificate hostname instead.
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate { get; set; }
}
