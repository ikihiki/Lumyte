using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Pipelines.RenderGraph;

/// <summary>Connects one opaque record-only unit to existing graph dependencies and coarse lifetimes.</summary>
public static class PipelineGraphExtensions
{
    /// <summary>Atomically adds one logical record-only pass after complete actual-resource preflight.</summary>
    /// <param name="graph">The building graph on the originating device.</param>
    /// <param name="name">The bounded diagnostic pass name.</param>
    /// <param name="plan">The caller-owned exclusive no-readback record plan.</param>
    /// <param name="bindings">Exactly one binding per external manifest slot.</param>
    /// <returns>The ordinary graph pass, supporting DependsOn and KeepAlive.</returns>
    public static RenderGraphPass AddPipelinePass(this Lumyte.Graphics.RenderGraph.RenderGraph graph, string name, GraphicsPipelineRecordPlan plan, PipelineGraphBindings bindings)
        => AddPipelinePassCore(graph, name, plan, bindings, null);

    internal static RenderGraphPass AddPipelinePassCore(Lumyte.Graphics.RenderGraph.RenderGraph graph, string name, GraphicsPipelineRecordPlan plan, PipelineGraphBindings bindings, Action<int>? faultInjection)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bindings);
        plan.ValidateGraphAppend();
        GraphicsRun run = plan.State.Run;
        graph.ValidateAppend(run.Device, name);
        graph.ValidateNativeOrigins(run.Device);
        GraphicsPipelineResourceManifest manifest = plan.Resources;
        if (bindings.Textures.Count + bindings.Buffers.Count != manifest.Count)
        {
            throw new ArgumentException("Bind every external manifest slot exactly once.", nameof(bindings));
        }

        var claimed = new HashSet<PipelineResourceSlot>(ReferenceEqualityComparer.Instance);
        var uses = new List<RenderGraphPassUse>();
        var textures = new Dictionary<object, TextureAlias>();
        foreach (PipelineGraphTextureBinding binding in bindings.Textures)
        {
            ArgumentNullException.ThrowIfNull(binding.Slot);
            ArgumentNullException.ThrowIfNull(binding.Resource);
            ArgumentNullException.ThrowIfNull(binding.Allocation);
            PipelineExternalResource resource = Slot(binding.Slot);
            if (resource is not PipelineTextureResource texture || !ReferenceEquals(texture.Allocation, binding.Allocation) || texture.States.Count != 1 || !texture.States.Contains(binding.State) || texture.Scope != binding.Scope)
            {
                throw new ArgumentException("Texture binding differs from the captured allocation, single state or complete scope.", nameof(bindings));
            }

            ValidateOrigin(binding.Allocation, run.Device);
            IGraphicsTexture imported = graph.GetImportedTexture(binding.Resource, run.Device);
            TextureViewInfo view = graph.GetImportedTextureView(binding.Resource, texture.View, run.Device);
            if (!ReferenceEquals(imported, binding.Allocation) || view != texture.ViewInfo)
            {
                throw new ArgumentException("The imported texture/view differs from the prepared native resource.", nameof(bindings));
            }

            object identity = ((IGraphicsNativeOrigin)binding.Allocation).AllocationIdentity;
            if (textures.TryGetValue(identity, out TextureAlias? alias))
            {
                if (!ReferenceEquals(alias.Resource, binding.Resource) || alias.State != binding.State)
                {
                    throw new ArgumentException("Aliased texture slots require one imported handle and one shared state.", nameof(bindings));
                }

                alias.Scope = new BarrierScope(alias.Scope.Stages | binding.Scope.Stages, alias.Scope.Access | binding.Scope.Access);
            }
            else
            {
                textures.Add(identity, new TextureAlias(binding.Resource, binding.State, binding.Scope));
            }
        }

        foreach (PipelineGraphBufferBinding binding in bindings.Buffers)
        {
            PipelineExternalResource resource = Slot(binding.Slot);
            ValidateOrigin(resource.Allocation, run.Device);
            uses.Add(binding.Validate(graph, resource, run.Device));
        }

        foreach (TextureAlias alias in textures.Values)
        {
            uses.Add(RenderGraphPassUse.Texture(alias.Resource, alias.State, alias.Scope));
        }

        graph.ValidatePassUses(name, run.Device, uses);
        using var pending = new AppendReservation(plan.ReserveGraph());
        RenderGraphPass pass = graph.AppendTransactional(name, run.Device, context => pending.Lifetime.Record(context.Commands), uses, pending.Lifetime, faultInjection is null ? null : stage => faultInjection((int)stage));
        pending.Commit();
        return pass;

        PipelineExternalResource Slot(PipelineResourceSlot slot)
        {
            if (!ReferenceEquals(slot.Owner, manifest) || !claimed.Add(slot))
            {
                throw new ArgumentException("Manifest slot is foreign or duplicated.", nameof(bindings));
            }

            PipelineExternalResource resource = manifest.Resources[slot.Index];
            if (!ReferenceEquals(resource.Device, run.Device) || !ReferenceEquals(resource.Generation, run.Generation))
            {
                throw new ArgumentException("Manifest resource device/generation mismatch.", nameof(bindings));
            }

            return resource;
        }
    }

    private static void ValidateOrigin(object resource, IGraphicDevice device)
    {
        if (resource is not IGraphicsNativeOrigin origin)
        {
            throw new NotSupportedException("The binding has no actual native origin contract.");
        }

        origin.ValidateOrigin();
        if (!ReferenceEquals(origin.Device, device))
        {
            throw new ArgumentException("The binding belongs to another actual native device.");
        }
    }

    private sealed class TextureAlias(RenderGraphTexture resource, TextureState state, BarrierScope scope)
    {
        internal RenderGraphTexture Resource { get; } = resource;

        internal TextureState State { get; } = state;

        internal BarrierScope Scope { get; set; } = scope;
    }

    private sealed class AppendReservation(GraphicsGraphReservation reservation) : IDisposable
    {
        private bool _committed;

        internal PipelineLifetime Lifetime { get; } = new(reservation);

        public void Dispose()
        {
            if (!_committed)
            {
                reservation.Dispose();
            }
        }

        internal void Commit()
        {
            reservation.Commit();
            _committed = true;
        }
    }

    private sealed class PipelineLifetime(GraphicsGraphReservation reservation) : IRenderGraphLifetime
    {
        public void Validate() => reservation.Validate();

        public void Dispose() => reservation.Dispose();

        internal void Record(IGraphicsCommandBuffer commands) => reservation.Record(commands);
    }
}
