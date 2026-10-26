namespace Lumyte.Diagnostics.Windowing;

/// <summary>Lists managed window IDs separated by line feeds.</summary>
/// <param name="Windows">The managed window IDs.</param>
public sealed record WindowDiagnosticWindowList(
    [property: DiagnosticMember(MaxLength = 16512)] string Windows);
