namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>A detached barrier inspection result.</summary>
/// <param name="PassIndex">The PassIndex value.</param>
/// <param name="ResourceIndex">The ResourceIndex value.</param>
/// <param name="BeforeState">The BeforeState value.</param>
/// <param name="AfterState">The AfterState value.</param>
/// <param name="BeforeStages">The BeforeStages value.</param>
/// <param name="BeforeAccess">The BeforeAccess value.</param>
/// <param name="AfterStages">The AfterStages value.</param>
/// <param name="AfterAccess">The AfterAccess value.</param>
public sealed record RenderGraphBarrierDiagnostic(long PassIndex, long ResourceIndex, string BeforeState, string AfterState, long BeforeStages, long BeforeAccess, long AfterStages, long AfterAccess);
