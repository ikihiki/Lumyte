namespace Lumyte.Diagnostics;

/// <summary>A detached subsystem schema for publication by a transport adapter.</summary>
/// <param name="Subsystem">The subsystem identity and schema version.</param>
/// <param name="Operations">The published operation descriptors.</param>
public sealed record DiagnosticSubsystemCatalog(SubsystemDescriptor Subsystem, IReadOnlyList<OperationDescriptor> Operations);
