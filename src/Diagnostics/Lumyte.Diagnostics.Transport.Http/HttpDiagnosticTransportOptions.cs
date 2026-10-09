namespace Lumyte.Diagnostics.Transport.Http;

/// <summary>HTTP connection settings supplied by the composition root.</summary>
public sealed class HttpDiagnosticTransportOptions
{
    /// <summary>Gets or sets the server base address.</summary>
    public Uri BaseAddress { get; set; } = null!;

    /// <summary>Gets or sets the game authentication credential.</summary>
    public string GameToken { get; set; } = string.Empty;
}
