using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class PipelineBatch2D(GraphicsPipelineRecordPlan plan, IGraphicsTextureView target, PipelineOwnerGroup2D owners) : IPreparedCommandBatch2D
{
    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (PipelineExternalResource resource in plan.Resources.Resources)
        {
            if (resource is PipelineSamplerResource sampler)
            {
                resources.UseSampler(sampler.Sampler);
                continue;
            }

            if (resource is not PipelineTextureResource texture || texture.States.Count != 1)
            {
                throw new NotSupportedException("The Drawing2D graph bridge requires single-state external texture declarations.");
            }

            TextureState state = texture.States.Single();
            if (state == TextureState.ColorAttachment && ReferenceEquals(texture.View.Texture, target.Texture))
            {
                // The receiving drawing pass already declares its active attachment.
                continue;
            }

            if (state == TextureState.Sampled)
            {
                resources.UseTextureView(texture.View, texture.Scope);
            }
            else
            {
                resources.UseTexture(texture.View.Texture, state, texture.Scope);
            }
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView actualTarget)
    {
        if (actualTarget is not IGraphicsNativeOrigin actual || target is not IGraphicsNativeOrigin expected ||
            !ReferenceEquals(actual.Device, expected.Device) || !Equals(actual.AllocationIdentity, expected.AllocationIdentity) || actualTarget.Info != target.Info)
        {
            throw new ArgumentException("Record target differs from the immutable preparation source binding.", nameof(actualTarget));
        }

        actual.ValidateOrigin();
        expected.ValidateOrigin();
        plan.Record(commands);
    }

    public void Dispose() => owners.Dispose();
}
