using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal static class BarrierPreparation
{
    internal static Dictionary<PlanPass, IReadOnlyList<PreparedPlanBarrier>> Prepare(CompiledGraphicsPlan plan, PreparedResources resources)
    {
        var result = new Dictionary<PlanPass, IReadOnlyList<PreparedPlanBarrier>>();
        var textureStates = new Dictionary<(ResourceIdentity, ulong), (TextureState State, BarrierScope Scope)>();
        foreach (PlanPass pass in plan.Passes)
        {
            var barriers = new List<PreparedPlanBarrier>();
            barriers.AddRange(resources.PhysicalBufferBarriers.Where(b => b.Pass == pass).Select(b => b.Barrier));
            foreach (PlanBarrier? edge in plan.Barriers.Where(b => b.After == pass && b.Destination.Resource is not PlanTexture))
            {
                ulong start = Math.Max(edge.Source.Range.Offset, edge.Destination.Range.Offset);
                ulong end = Math.Min(edge.Source.Range.End, edge.Destination.Range.End);
                resources.ReserveBarrier();
                barriers.Add(edge.Destination.Resource.PrepareBarrier(resources, new(start, end - start), edge.Source.Scope, edge.Destination.Scope, default, default));
            }

            foreach (PlanResourceUse? use in pass.Uses.Concat(resources.PhysicalTextureUses.Where(p => p.Pass == pass).Select(p => p.Use)).Where(u => u.Resource is PlanTexture))
            {
                for (ulong subresource = use.Range.Offset; subresource < use.Range.End; subresource++)
                {
                    (ResourceIdentity Identity, ulong Subresource) key = (use.Resource.Identity, subresource);
                    (TextureState, BarrierScope) before = textureStates.TryGetValue(key, out (TextureState State, BarrierScope Scope) previous) ? previous : (use.Resource.Identity.InitialTextureState, new BarrierScope(PipelineStage.AllCommands, ResourceAccess.None));
                    TextureState state = use.TextureState ?? throw new InvalidOperationException("Missing texture state.");
                    resources.ReserveBarrier();
                    barriers.Add(use.Resource.PrepareBarrier(resources, new(subresource, 1), before.Item2, use.Scope, before.Item1, state));
                    textureStates[key] = (state, use.Scope);
                }
            }

            result.Add(pass, barriers.AsReadOnly());
        }

        foreach (PlanTextureExport export in plan.TextureExports)
        {
            for (ulong subresource = export.Texture.Range.Offset; subresource < export.Texture.Range.End; subresource++)
            {
                (ResourceIdentity Identity, ulong Subresource) key = (export.Texture.Identity, subresource);
                (TextureState State, BarrierScope Scope) before = textureStates.TryGetValue(key, out (TextureState State, BarrierScope Scope) previous)
                    ? previous : (export.Texture.Identity.InitialTextureState, new(PipelineStage.AllCommands, ResourceAccess.None));
                resources.ReserveBarrier();
                resources.FinalBarriers.Add(export.Texture.PrepareBarrier(resources, new(subresource, 1), before.Scope, export.FinalScope, before.State, export.FinalState));
                textureStates[key] = (export.FinalState, export.FinalScope);
            }
        }

        foreach (PlanResourceExport export in plan.ResourceExports)
        {
            BarrierScope before = new(PipelineStage.AllCommands, ResourceAccess.None);
            foreach (PlanResourceUse use in plan.Passes.SelectMany(p => p.Uses).Where(u => u.Resource.Identity == export.Resource.Identity && u.Range.Overlaps(export.Resource.Range)))
            {
                before = new(before.Stages | use.Scope.Stages, before.Access | use.Scope.Access);
            }

            resources.ReserveBarrier();
            resources.FinalBarriers.Add(export.Resource.PrepareBarrier(resources, export.Resource.Range, before, export.FinalScope, default, default));
        }

        return result;
    }
}
