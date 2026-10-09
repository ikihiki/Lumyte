namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies execution stages and memory access for an explicit dependency.</summary>
/// <param name="Stages">The execution stages.</param>
/// <param name="Access">The memory accesses.</param>
public readonly record struct BarrierScope(PipelineStage Stages, ResourceAccess Access);
