using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Resources.Graphics.AotSmoke;

/// <summary>Real consumer-assembly factories; no partial extension of the core factory.</summary>
public static partial class CustomCompose
{
    /// <summary>Passive consumer component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>A bounded path algorithm configuration, not a processor-sharing handle.</summary>
        [Composable(Factory = "CustomCompose")]
        public sealed partial class Paths : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            /// <summary>Gets the explicit compatibility configuration.</summary>
            [ComposeParameter]
            public required int MaxSegments { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(PathsProcessor);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(MaxSegments, 1);
                return new PathsProcessor(context.GetSharedService(new PathServiceRequest(MaxSegments)));
            }
        }

        /// <summary>A pure rectangle candidate configuration.</summary>
        [Composable(Factory = "CustomCompose")]
        public sealed partial class FastRectangles : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            /// <summary>Gets a value indicating whether this candidate admits the batch.</summary>
            [ComposeParameter]
            public required bool Accepts { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(RectangleProcessor);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new RectangleProcessor(Accepts);
        }
    }

    private sealed class PathServiceRequest(int maxSegments) : PipelineServiceRequest2D<PathManager, int>("readonly-path-algorithm", maxSegments, 1)
    {
        protected override PathManager CreateService(PipelineBuildContext2D context) => new(Options);
    }

    private sealed class PathManager(int maxSegments) : PipelineSharedService2D
    {
        private readonly IFillPathRenderer2D _fill = new DefaultFillPathRenderer2D();
        private readonly IStrokePathRenderer2D _stroke = new DefaultStrokePathRenderer2D();
        private bool _disposed;

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
        {
            Validate();
            foreach (FillPathCommand2D command in commands.Span)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(command.Path.Segments.Count, maxSegments);
            }

            return _fill.PrepareAsync(context, commands, cancellationToken);
        }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken)
        {
            Validate();
            foreach (StrokePathCommand2D command in commands.Span)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(command.Path.Segments.Count, maxSegments);
            }

            return _stroke.PrepareAsync(context, commands, cancellationToken);
        }

        public void Validate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        }

        public override void Dispose() => _disposed = true;
    }

    private sealed class PathsProcessor(PathManager manager) : PipelineElement2D, IFillPathRenderer2D, IStrokePathRenderer2D
    {
        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
            => new PathBatch(await manager.PrepareAsync(context, commands, cancellationToken), manager);

        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
            => new PathBatch(await manager.PrepareAsync(context, commands, cancellationToken), manager);
    }

    private sealed class PathBatch(IPreparedCommandBatch2D batch, PathManager manager) : IPreparedCommandBatch2D
    {
        public void DeclareResources(IDrawingResourceDeclarations2D resources) => batch.DeclareResources(resources);

        public void Record(Lumyte.Graphics.Abstractions.IGraphicsCommandBuffer commands, Lumyte.Graphics.Abstractions.IGraphicsTextureView target)
        {
            manager.Validate();
            batch.Record(commands, target);
        }

        public void Dispose() => batch.Dispose();
    }

    private sealed class RectangleProcessor(bool accepts) : PipelineElement2D, IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
    {
        private readonly IFillRectRenderer2D _standard = new DefaultFillRectRenderer2D();

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => accepts;

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => _standard.PrepareAsync(context, commands, cancellationToken);
    }
}
