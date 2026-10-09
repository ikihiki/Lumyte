namespace Lumyte.Diagnostics.Server;

/// <summary>Loopback server credentials and capability policy.</summary>
public sealed class DiagnosticServerOptions
{
    /// <summary>Gets or sets the operator HTTP port; zero selects an ephemeral port.</summary>
    public int HttpPort { get; set; } = 5000;

    /// <summary>Gets or sets the game HTTP/2 port; zero selects an ephemeral port.</summary>
    public int MagicOnionPort { get; set; } = 5001;

    /// <summary>Gets or sets the game enrollment credential.</summary>
    public string GameToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator credential, distinct from the game credential.</summary>
    public string OperatorToken { get; set; } = string.Empty;

    /// <summary>Gets or sets explicitly allowed browser origins; empty disables cross-origin access.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Gets or sets allowed session capabilities.</summary>
    public DiagnosticPermission[] GamePermissions { get; set; } = Enum.GetValues<DiagnosticPermission>();

    /// <summary>Gets or sets allowed operator capabilities.</summary>
    public DiagnosticPermission[] OperatorPermissions { get; set; } = Enum.GetValues<DiagnosticPermission>();
}
