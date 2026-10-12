namespace Lumyte.Diagnostics.Animation;

/// <summary>Lists registered animation target IDs separated by line feeds.</summary>
/// <param name="Targets">The target IDs.</param>
public sealed record AnimationDiagnosticTargetList(
    [property: DiagnosticMember(MaxLength = 16512)] string Targets);
