using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan resource.</summary>
public abstract class PlanResource
{
    internal PlanResource(GraphicsPlanBuilder owner, ResourceIdentity identity, int version, PlanRange range, IReadOnlyList<PlanRange> initialized)
    {
        (Owner, Identity, Version, Range, Initialized) = (owner, identity, version, range, initialized);
    }

    /// <summary>Gets version.</summary>
    public int Version { get; }

    /// <summary>Gets range.</summary>
    public PlanRange Range { get; }

    internal GraphicsPlanBuilder Owner { get; }

    internal ResourceIdentity Identity { get; }

    internal IReadOnlyList<PlanRange> Initialized { get; }

    internal PlanPass? Producer { get; set; }

    internal virtual IReadOnlyList<PlanResourceUse> ReferenceUses => [];

    internal virtual ResourceAccess PrecedingPhysicalWrites(PreparedResources resources) => ResourceAccess.None;

    internal bool SameVersion(PlanResource other) => Owner == other.Owner && Identity == other.Identity && Version == other.Version;

    internal virtual void ValidateExport(IReadOnlySet<PlanPass> live)
    {
    }

    internal abstract PlanResource NextVersion(int version, IReadOnlyList<PlanRange> initialized, bool preserve);

    internal abstract PreparedPlanBarrier PrepareBarrier(PreparedResources resources, PlanRange range, BarrierScope before, BarrierScope after, TextureState beforeState, TextureState afterState);
}
