using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal static class PreparedBindingClosure
{
    internal static void Complete(PreparedResources resources)
    {
        PhysicalBindingUse[] original = resources.PhysicalBindings.ToArray();
        PreparedOperation[] registered = resources.Operations.Where(o => original.Any(b => b.Operation == o.Index)).ToArray();
        resources.PhysicalBindings.Clear();
        foreach (IGrouping<IArgumentTable, PreparedOperation> group in registered.GroupBy(o => o.Reservation.Table))
        {
            int[] indices = group.Select(o => o.Index).ToArray();
            PhysicalBindingUse[] bindings = original.Where(b => indices.Contains(b.Operation)).DistinctBy(b => (b.Allocation, b.Access)).ToArray();
            foreach (PreparedOperation operation in group)
            {
                foreach (PhysicalBindingUse binding in bindings)
                {
                    if (resources.PhysicalBindings.Count >= resources.Budget.MaximumPhysicalBindingUses)
                    {
                        throw new InvalidOperationException("Whole-table physical binding reservation exceeded.");
                    }

                    resources.PhysicalBindings.Add(new(operation.Index, binding.Allocation, binding.Access));
                    if (!operation.BindsTable)
                    {
                        continue;
                    }

                    if (binding.Allocation is IGraphicsTextureView view)
                    {
                        PlanTexture texture = resources.Views.First(pair => ReferenceEquals(pair.Value, view)).Key;
                        if (operation.Pass.Uses.Any(u => u.Resource.Identity == texture.Identity && u.Range.Overlaps(texture.Range) && u.TextureState != TextureState.Sampled))
                        {
                            throw new NotSupportedException("A coherent whole table exposes an attachment/copy texture in a sampled domain; separate the payload/table domains explicitly.");
                        }

                        PipelineStage stages = AvailableStages(operation.Pass, operation.Graphics ? PipelineStage.AllGraphics : PipelineStage.ComputeShader);
                        resources.PhysicalTextureUses.Add((operation.Pass, new(texture, texture.Range, PlanAccess.Read, new(stages, ResourceAccess.ShaderRead)) { TextureState = TextureState.Sampled }));
                    }
                    else if (binding.Allocation is not IGraphicsSampler)
                    {
                        ResourceIdentity identity = resources.Allocations.First(pair => ReferenceEquals(pair.Value, binding.Allocation)).Key;
                        PlanResource resource = identity.Versions[0];
                        PipelineStage stages = AvailableStages(operation.Pass, operation.Graphics ? PipelineStage.AllGraphics : PipelineStage.ComputeShader);
                        ResourceAccess access = ((binding.Access & ArgumentBufferAccess.Read) != 0 ? ResourceAccess.ShaderRead : ResourceAccess.None)
                            | ((binding.Access & ArgumentBufferAccess.Write) != 0 ? ResourceAccess.ShaderWrite : ResourceAccess.None);
                        resources.ReserveBarrier();
                        PreparedPlanBarrier barrier = resource.PrepareBarrier(resources, resource.Range, new(AvailableStages(operation.Pass, PipelineStage.AllCommands | PipelineStage.Host), resource.PrecedingPhysicalWrites(resources)), new(stages, access), default, default);
                        resources.PhysicalBufferBarriers.Add((operation.Pass, barrier));
                    }
                }
            }
        }
    }

    private static PipelineStage AvailableStages(PlanPass pass, PipelineStage stages)
    {
        GraphicsFeatures features = pass.Owner.Frame.Session.Device.Caps.Features;
        if ((features & GraphicsFeatures.MeshShader) == 0)
        {
            stages &= ~PipelineStage.MeshShader;
        }

        if ((features & GraphicsFeatures.TaskShader) == 0)
        {
            stages &= ~PipelineStage.TaskShader;
        }

        return stages;
    }
}
