namespace Lumyte.Graphics.RenderGraph;

public sealed partial class GpuRenderGraph
{
    private Dictionary<GpuRenderGraphResource, int>? structureResourceIndices;

    internal void RequireOwnedResource(GpuRenderGraphResource resource) => RequireResource(resource);

    internal ulong CreateStructureHash()
    {
        var hash = new GpuRenderGraphStructureHasher();
        hash.Add(resources.Count);
        foreach (GpuRenderGraphResourceDeclaration declaration in resources)
        {
            GpuRenderGraphResourceInfo info = declaration.Info;
            hash.Add(info.Name);
            hash.Add((int)info.Kind);
            hash.Add((int)info.MemoryKind);
            hash.Add(info.IsTransient);
            hash.Add(info.IsExported);
            hash.Add(outputs.Contains(info.Resource));
            if (info.TextureDescription is { } texture)
            {
                hash.Add(true);
                hash.Add(texture.Width);
                hash.Add(texture.Height);
                hash.Add((int)texture.Format);
                hash.Add((int)texture.Usage);
                hash.Add(texture.MipCount);
                hash.Add(texture.LayerCount);
                hash.Add(texture.SampleCount);
            }
            else { hash.Add(false); }
            if (info.BufferDescription is { } buffer)
            {
                hash.Add(true);
                hash.Add(buffer.Size);
                hash.Add((int)buffer.Usage);
            }
            else { hash.Add(false); }
        }

        hash.Add(passes.Count);
        foreach (GpuRenderGraphPassDeclaration pass in passes)
        {
            hash.Add(pass.Name);
            hash.Add((int)pass.Flags);
            hash.Add(pass.Accesses.Count);
            foreach (GpuRenderGraphResourceAccess access in pass.Accesses)
            {
                hash.Add(GetStructureResourceIndex(access.Resource));
                hash.Add((int)access.Access);
                hash.Add((uint)access.Stage);
                hash.Add((int)access.Hazards);
            }
        }
        return hash.Value;
    }

    internal GpuRenderGraphStructure CaptureStructure(ulong hash)
    {
        var resourceStructures = new GpuRenderGraphResourceStructure[resources.Count];
        for (int index = 0; index < resources.Count; index++)
        {
            GpuRenderGraphResourceInfo info = resources[index].Info;
            resourceStructures[index] = new(
                info.Name,
                info.Kind,
                info.MemoryKind,
                info.IsTransient,
                info.IsExported,
                outputs.Contains(info.Resource),
                info.TextureDescription,
                info.BufferDescription);
        }

        var passStructures = new GpuRenderGraphPassStructure[passes.Count];
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            GpuRenderGraphPassDeclaration pass = passes[passIndex];
            var accesses = new GpuRenderGraphAccessStructure[pass.Accesses.Count];
            for (int accessIndex = 0; accessIndex < pass.Accesses.Count; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                accesses[accessIndex] = new(
                    GetStructureResourceIndex(access.Resource),
                    access.Access,
                    access.Stage,
                    access.Hazards);
            }
            passStructures[passIndex] = new(pass.Name, pass.Flags, accesses);
        }
        return new(hash, resourceStructures, passStructures);
    }

    internal bool MatchesStructure(GpuRenderGraphStructure structure)
    {
        if (resources.Count != structure.Resources.Length || passes.Count != structure.Passes.Length)
        {
            return false;
        }
        for (int index = 0; index < resources.Count; index++)
        {
            GpuRenderGraphResourceInfo info = resources[index].Info;
            GpuRenderGraphResourceStructure expected = structure.Resources[index];
            if (!StringComparer.Ordinal.Equals(info.Name, expected.Name)
                || info.Kind != expected.Kind
                || info.MemoryKind != expected.MemoryKind
                || info.IsTransient != expected.IsTransient
                || info.IsExported != expected.IsExported
                || outputs.Contains(info.Resource) != expected.IsOutput
                || info.TextureDescription != expected.TextureDescription
                || info.BufferDescription != expected.BufferDescription)
            {
                return false;
            }
        }
        for (int passIndex = 0; passIndex < passes.Count; passIndex++)
        {
            GpuRenderGraphPassDeclaration pass = passes[passIndex];
            GpuRenderGraphPassStructure expected = structure.Passes[passIndex];
            if (!StringComparer.Ordinal.Equals(pass.Name, expected.Name)
                || pass.Flags != expected.Flags
                || pass.Accesses.Count != expected.Accesses.Length)
            {
                return false;
            }
            for (int accessIndex = 0; accessIndex < pass.Accesses.Count; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                GpuRenderGraphAccessStructure expectedAccess = expected.Accesses[accessIndex];
                if (GetStructureResourceIndex(access.Resource) != expectedAccess.ResourceIndex
                    || access.Access != expectedAccess.Access
                    || access.Stage != expectedAccess.Stage
                    || access.Hazards != expectedAccess.Hazards)
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal GpuRenderGraphPlan BindCachedPlan(
        GpuRenderGraphPlan template,
        IReadOnlyDictionary<GpuRenderGraphResource, int> templateResourceIndices)
    {
        if (template.Resources.Count != resources.Count)
        {
            throw new InvalidOperationException("Cached render-graph structure does not match this frame.");
        }

        GpuRenderGraphResource Map(GpuRenderGraphResource resource)
            => resources[templateResourceIndices[resource]].Info.Resource;

        var passPlans = new GpuRenderGraphPassPlan[template.Passes.Count];
        var currentRecorders = new IGpuRenderGraphPassRecorder[passPlans.Length];
        for (int index = 0; index < passPlans.Length; index++)
        {
            GpuRenderGraphPassPlan pass = template.Passes[index];
            var accesses = new GpuRenderGraphResourceAccess[pass.Accesses.Count];
            for (int accessIndex = 0; accessIndex < accesses.Length; accessIndex++)
            {
                GpuRenderGraphResourceAccess access = pass.Accesses[accessIndex];
                accesses[accessIndex] = access with { Resource = Map(access.Resource) };
            }
            passPlans[index] = new(pass.Name, pass.DeclarationIndex, accesses);
            currentRecorders[index] = passes[pass.DeclarationIndex];
        }

        var barriers = new GpuRenderGraphBarrierPlan[template.Barriers.Count];
        for (int index = 0; index < barriers.Length; index++)
        {
            GpuRenderGraphBarrierPlan barrier = template.Barriers[index];
            var barrierResources = new GpuRenderGraphResource[barrier.Resources.Count];
            for (int resourceIndex = 0; resourceIndex < barrierResources.Length; resourceIndex++)
            {
                barrierResources[resourceIndex] = Map(barrier.Resources[resourceIndex]);
            }
            barriers[index] = new(
                barrier.DestinationPass,
                barrier.Before,
                barrier.After,
                barrier.Hazards,
                barrierResources);
        }

        var transientResources = new GpuRenderGraphTransientResourcePlan[template.TransientResources.Count];
        for (int index = 0; index < transientResources.Length; index++)
        {
            GpuRenderGraphTransientResourcePlan resource = template.TransientResources[index];
            transientResources[index] = resource with { Resource = Map(resource.Resource) };
        }

        var transientSlots = new GpuRenderGraphTransientSlotPlan[template.TransientSlots.Count];
        for (int index = 0; index < transientSlots.Length; index++)
        {
            GpuRenderGraphTransientSlotPlan slot = template.TransientSlots[index];
            var slotResources = new GpuRenderGraphResource[slot.Resources.Count];
            for (int resourceIndex = 0; resourceIndex < slotResources.Length; resourceIndex++)
            {
                slotResources[resourceIndex] = Map(slot.Resources[resourceIndex]);
            }
            transientSlots[index] = new(
                slot.Slot,
                slot.Kind,
                slot.MemoryKind,
                slot.TextureDescription,
                slot.BufferDescription,
                slotResources);
        }

        var aliasBarriers = new GpuRenderGraphAliasBarrierPlan[template.AliasBarriers.Count];
        for (int index = 0; index < aliasBarriers.Length; index++)
        {
            GpuRenderGraphAliasBarrierPlan barrier = template.AliasBarriers[index];
            aliasBarriers[index] = new(
                barrier.DestinationPass,
                barrier.ReuseSlot,
                Map(barrier.BeforeResource),
                Map(barrier.AfterResource),
                barrier.Before,
                barrier.After,
                barrier.Hazards);
        }

        var resourceInfos = new GpuRenderGraphResourceInfo[resources.Count];
        for (int index = 0; index < resourceInfos.Length; index++)
        {
            resourceInfos[index] = resources[index].Info with { };
        }
        return new(
            resourceInfos,
            passPlans,
            barriers,
            transientResources,
            transientSlots,
            aliasBarriers,
            currentRecorders);
    }

    private int GetStructureResourceIndex(GpuRenderGraphResource resource)
    {
        if (structureResourceIndices is null || structureResourceIndices.Count != resources.Count)
        {
            var indices = new Dictionary<GpuRenderGraphResource, int>(resources.Count);
            for (int index = 0; index < resources.Count; index++)
            {
                indices.Add(resources[index].Info.Resource, index);
            }
            structureResourceIndices = indices;
        }
        return structureResourceIndices[resource];
    }

}
