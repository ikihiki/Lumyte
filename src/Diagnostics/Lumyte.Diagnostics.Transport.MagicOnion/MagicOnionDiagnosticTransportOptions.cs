namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Desktop connection settings supplied by the composition root.</summary>
public sealed class MagicOnionDiagnosticTransportOptions
{
    /// <summary>Gets or sets the HTTP/2 server endpoint.</summary>
    public Uri Endpoint { get; set; } = null!;

    /// <summary>Gets or sets the game authentication credential.</summary>
    public string GameToken { get; set; } = string.Empty;
}
