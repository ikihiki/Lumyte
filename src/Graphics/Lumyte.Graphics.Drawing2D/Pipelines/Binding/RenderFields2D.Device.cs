using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Projects borrowed native source bindings without a transfer or submission.</summary>
public static partial class RenderFields2D
{
    /// <summary>Extracts the exact source snapshot's borrowed device views.</summary>
    /// <returns>The pure native-view projection.</returns>
    public static IStage<RenderRequest2D, Device<ResourceViewsV1>> ResourceViews() => new ViewProjection<ResourceViewsV1>(false);

    /// <summary>Extracts the borrowed native target and its immutable source/grid identity.</summary>
    /// <returns>The pure target projection.</returns>
    public static IStage<RenderRequest2D, Device<TargetV1>> Target() => new ViewProjection<TargetV1>(true);

    private sealed class ViewProjection<TSchema>(bool target) : IStage<RenderRequest2D, Device<TSchema>>, IStageFactory<RenderRequest2D, Device<TSchema>>, IStageProcessor<RenderRequest2D, Device<TSchema>>
        where TSchema : ISchema
    {
        public IStageFactory<RenderRequest2D, Device<TSchema>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            long count = target ? 1 : inputs.Extents[0].Metadata.ContainsKey("UpperBound") ? 1048576 :
                inputs.Extents[0].Metadata.TryGetValue("viewCount", out object? views) ? Convert.ToInt64(views) : 0;
            return new StageRequirements(checked(1024 + (count * 384)), []);
        }

        public IStageProcessor<RenderRequest2D, Device<TSchema>> Create(StageBuildContext context)
        {
            if (context.Capabilities is not IGraphicsStageCapabilities)
            {
                throw new NotSupportedException("Native source projection requires the explicit graphics host.");
            }

            return new ViewProjection<TSchema>(target);
        }

        public ValueTask<ValueLease<Device<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderRequest2D request = context.Read(a);
            RenderPreparationContext2D preparation = request.Context ?? throw new InvalidOperationException("A native projection requires a renderer-owned source binding.");
            preparation.ValidateResources();
            RenderSourceBinding2D source = preparation.SourceBinding ?? throw new InvalidOperationException("The target source binding is missing.");
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            IReadOnlyList<IGraphicsTextureView> views = target ? [source.Target] : request.ResourceSnapshot.Views;
            Dictionary<string, object> metadata = request.Metadata();
            if (!target)
            {
                metadata.Add("resourceSnapshot", request.ResourceSnapshot);
            }
            else
            {
                LRgba<double> clear = request.Target.ClearColor.ToLinear<double>().PremultiplyLinear<double>();
                metadata.Add("loadOp", (int)request.Target.LoadOp);
                metadata.Add("clearRed", clear.R);
                metadata.Add("clearGreen", clear.G);
                metadata.Add("clearBlue", clear.B);
                metadata.Add("clearAlpha", clear.A);
                metadata.Add("targetFormat", (int)source.Target.Info.Format);
            }

            SchemaExtent extent = new(views.Count, metadata);
            return ValueTask.FromResult(graphics.Preparation.BindExternalTextures<TSchema>(views, extent, target ? null : request.ResourceSnapshot.Samplers));
        }

        public void Dispose()
        {
        }
    }
}
