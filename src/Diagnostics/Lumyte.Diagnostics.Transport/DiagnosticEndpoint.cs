namespace Lumyte.Diagnostics.Transport;

/// <summary>Validates endpoints before sending diagnostic credentials.</summary>
public static class DiagnosticEndpoint
{
    /// <summary>Requires HTTPS except for explicit loopback development listeners.</summary>
    /// <param name="endpoint">The server endpoint.</param>
    public static void Validate(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0
            || (endpoint.Scheme != "https" && (endpoint.Scheme != "http" || !endpoint.IsLoopback)))
        {
            throw new ArgumentException("Use HTTPS, or HTTP on loopback for local development.", nameof(endpoint));
        }
    }
}
