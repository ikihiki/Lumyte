namespace Lumyte.Diagnostics.Windowing;

/// <summary>Lists registered WindowSystem IDs separated by line feeds.</summary>
/// <param name="Systems">The registered system IDs.</param>
public sealed record WindowDiagnosticSystemList(
    [property: DiagnosticMember(MaxLength = 8256)] string Systems);
