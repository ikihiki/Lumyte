using System.Numerics;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Pure host field extraction stages; no upload, resource allocation or submit occurs.</summary>
public static partial class RenderFields2D
{
    /// <summary>Extracts contiguous logical ordinals with an explicitly host-only immutable command snapshot.</summary>
    /// <returns>A pure immutable host projection declaration.</returns>
    public static IStage<RenderRequest2D, Host<CommandsV1>> Commands() => new Projection<CommandsV1, uint>(r => [.. Enumerable.Range(0, r.Commands.Count).Select(i => (uint)i)], true, extent => extent.ElementCount);

    /// <summary>Extracts the source identity manifest. Device views are supplied independently by the renderer binding.</summary>
    /// <returns>A pure immutable host projection declaration.</returns>
    public static IStage<RenderRequest2D, Host<ResourceManifestV1>> ResourceManifest() => new Projection<ResourceManifestV1, uint>(r => [(uint)r.ResourceSnapshot.Views.Count, (uint)r.ResourceSnapshot.Samplers.Count], false, _ => 2);

    /// <summary>Extracts extent, pixel transform and the fixed sample/grid version.</summary>
    /// <returns>A pure immutable host projection declaration.</returns>
    public static IStage<RenderRequest2D, Host<FrameV1>> Frame() => new Projection<FrameV1, Vector4>(r => r.FrameCells(), false, _ => 2);

    private sealed class Projection<TSchema, T>(Func<RenderRequest2D, T[]> select, bool snapshot, Func<SchemaExtent, long> count) : IStage<RenderRequest2D, Host<TSchema>>, IStageFactory<RenderRequest2D, Host<TSchema>>, IStageProcessor<RenderRequest2D, Host<TSchema>>
        where TSchema : ISchema
        where T : unmanaged
    {
        public IStageFactory<RenderRequest2D, Host<TSchema>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs)
        {
            long elements = inputs.Extents[0].Metadata.ContainsKey("UpperBound") ? 1048576 : count(inputs.Extents[0]);
            long bytes = checked((elements * System.Runtime.CompilerServices.Unsafe.SizeOf<T>() * 3) + 512);
            return new StageRequirements(bytes, []) { OutputMaxElements = elements };
        }

        public IStageProcessor<RenderRequest2D, Host<TSchema>> Create(StageBuildContext context) => new Projection<TSchema, T>(select, snapshot, count);

        public ValueTask<ValueLease<Host<TSchema>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderRequest2D r = context.Read(a);
            if (r.Commands.Count > 1048576)
            {
                throw new NotSupportedException("Logical command snapshot exceeds finite projection ceiling.");
            }

            T[] cells = select(r);
            SchemaExtent extent = r.Extent(cells.Length, snapshot);
            if (typeof(TSchema) == typeof(ResourceManifestV1))
            {
                Dictionary<string, object> metadata = r.Metadata();
                metadata.Add("resourceSnapshot", r.ResourceSnapshot);
                extent = new(cells.Length, metadata);
            }

            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<TSchema, T>(cells, extent)));
        }

        public void Dispose()
        {
        }
    }
}
