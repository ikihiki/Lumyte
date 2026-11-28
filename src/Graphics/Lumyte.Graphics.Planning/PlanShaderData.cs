using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>An immutable typed shader-data allocation version with declaration-time reference recipes.</summary>
/// <typeparam name="T">The original shader element ABI.</typeparam>
public sealed class PlanShaderData<T> : PlanResource
    where T : struct, IShaderArguments
{
    internal PlanShaderData(GraphicsPlanBuilder owner, ResourceIdentity identity, int version, PlanRange range, IReadOnlyList<PlanRange> initialized, ShaderArtifact artifact, MemoryPreference memory, ShaderDataPayload<T>? payload = null)
        : base(owner, identity, version, range, initialized)
    {
        (Artifact, Memory, Payload) = (artifact, memory, payload);
    }

    /// <summary>Gets the declared memory purpose.</summary>
    public MemoryPreference Memory { get; }

    internal ShaderArtifact Artifact { get; }

    internal ShaderDataPayload<T>? Payload { get; private set; }

    internal override IReadOnlyList<PlanResourceUse> ReferenceUses => Payload?.ReferenceUses ?? [];

    internal override void ValidateExport(IReadOnlySet<PlanPass> live)
    {
        if (T.ReferenceCount != 0 && Payload is null)
        {
            throw new InvalidOperationException("Exported reference-bearing shader data requires a known canonical payload closure.");
        }

        foreach (PlanResourceUse use in ShaderDataClosure.Collect(this, Owner.Frame.Session.Budget))
        {
            if (use.Access == PlanAccess.Write)
            {
                continue;
            }

            if (!Owner.IsInitialized(use.Resource, use.Range) || (use.Resource.Producer is { } producer && !live.Contains(producer)))
            {
                throw new InvalidOperationException("An exported shader-data read target must be initialized by an explicitly live producer; declare KeepAlive/export rather than reviving it from physical exposure.");
            }

            GraphicsPlanBuilder.ValidateNotOverwritten(use.Resource, use.Range, live);
        }
    }

    internal void InstallPayload(PlanRange range, IReadOnlyList<IArgumentPiece>[] values)
    {
        Owner.Mutable();
        Payload = new(Payload, range, values);
    }

    internal void InstallCopiedPayload(PlanRange range, IReadOnlyList<PlanResourceUse> references)
    {
        Owner.Mutable();
        Payload = new(Payload, range, [], references);
    }

    internal override ResourceAccess PrecedingPhysicalWrites(PreparedResources resources)
        => Memory == MemoryPreference.Upload ? ResourceAccess.HostWrite : ResourceAccess.CopyWrite;

    internal override PlanResource NextVersion(int version, IReadOnlyList<PlanRange> initialized, bool preserve)
        => new PlanShaderData<T>(Owner, Identity, version, Range, initialized, Artifact, Memory, preserve ? Payload : null);

    internal override PreparedPlanBarrier PrepareBarrier(PreparedResources resources, PlanRange range, BarrierScope before, BarrierScope after, TextureState beforeState, TextureState afterState)
        => new PreparedShaderDataBarrier<T>(new() { Buffer = resources.ShaderData(this, range), Before = before, After = after });
}
