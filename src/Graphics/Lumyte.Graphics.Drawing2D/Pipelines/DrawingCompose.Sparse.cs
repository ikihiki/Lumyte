using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive CPU sparse-strip factories.</summary>
public static partial class DrawingCompose
{
    /// <summary>Creates explicit CPU cache and strip limits.</summary>
    /// <param name="maxCachedBytes">Maximum idle geometry bytes.</param>
    /// <param name="maxCachedAssets">Maximum idle geometry assets.</param>
    /// <param name="maxStrips">Maximum output strips per batch.</param>
    /// <param name="maxBoundaryMasks">Maximum boundary pixel masks per batch.</param>
    /// <returns>Validated immutable CPU options.</returns>
    public static SparseCpuOptions2D CpuSparse(long maxCachedBytes, int maxCachedAssets, int maxStrips, int maxBoundaryMasks)
        => new SparseCpuOptions2D(maxCachedBytes, maxCachedAssets, maxStrips, maxBoundaryMasks).Validate();

    /// <summary>Passive sparse algorithm declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>CPU correlated sparse strips for FillRect.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseFillRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseFillRectElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseFillRectElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for FillRoundedRect.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseFillRoundedRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRoundedRectCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseFillRoundedRectElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseFillRoundedRectElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for FillCircle.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseFillCircle : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseFillCircleElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseFillCircleElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawLine.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawLine : PipelineDeclaration2D, IPipelineDeclaration2D<DrawLineCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawLineElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawLineElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for FillPath.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseFillPath : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseFillPathElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseFillPathElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for StrokePath.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseStrokePath : PipelineDeclaration2D, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseStrokePathElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseStrokePathElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawImage.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawImage : PipelineDeclaration2D, IPipelineDeclaration2D<DrawImageCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawImageElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawImageElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawCoverageMask.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawCoverageMask : PipelineDeclaration2D, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawCoverageMaskElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawCoverageMaskElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawDistanceField.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawDistanceField : PipelineDeclaration2D, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawDistanceFieldElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawDistanceFieldElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawMesh.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawMesh : PipelineDeclaration2D, IPipelineDeclaration2D<DrawMeshCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawMeshElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawMeshElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }

        /// <summary>CPU correlated sparse strips for DrawPaintGraph.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class SparseDrawPaintGraph : PipelineDeclaration2D, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        {
            /// <summary>Gets required CPU limits.</summary>
            [ComposeParameter]
            public required SparseCpuOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(SparseDrawPaintGraphElement2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
                => new SparseDrawPaintGraphElement2D(context.GetSharedService(new SparseCpuServiceRequest2D(Options.Validate())));
        }
    }
}
