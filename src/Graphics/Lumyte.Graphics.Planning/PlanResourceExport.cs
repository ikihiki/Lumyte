using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>An observable resource version with an explicit receiving external access scope.</summary>
/// <param name="Resource">The exact immutable buffer or shader-data range.</param>
/// <param name="FinalScope">The external access, such as host read after readback.</param>
public sealed record PlanResourceExport(PlanResource Resource, BarrierScope FinalScope);
