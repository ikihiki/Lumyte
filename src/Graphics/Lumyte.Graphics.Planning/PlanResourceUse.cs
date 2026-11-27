using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;
/// <summary>Declares plan resource use.</summary>
/// <param name = "Resource">The resource.</param>
/// <param name = "Range">The range.</param>
/// <param name = "Access">The access.</param>
/// <param name = "Scope">The scope.</param>
public sealed record PlanResourceUse(PlanResource Resource, PlanRange Range, PlanAccess Access, BarrierScope Scope)
{
    /// <summary>Gets texture state.</summary>
    public TextureState? TextureState { get; init; }
}
