using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive mesh coverage algorithm declarations.</summary>
public static partial class DrawingCompose
{
    /// <summary>Creates immutable Mesh admission and resource bounds with no implicit values.</summary>
    /// <param name="maxCommands">Maximum logical commands per candidate batch.</param>
    /// <param name="maxWorkingBytes">Conservative preparation working bound.</param>
    /// <param name="numericPoolBytes">Maximum idle numeric stream storage.</param>
    /// <returns>The validated passive options.</returns>
    public static MeshCoverageOptions2D MeshCoverageLimits(int maxCommands, long maxWorkingBytes, long numericPoolBytes)
    {
        var options = new MeshCoverageOptions2D(maxCommands, maxWorkingBytes, numericPoolBytes);
        options.Validate();
        return options;
    }

    /// <summary>Passive mesh algorithms; each placement has independent processing state.</summary>
    public static partial class Definitions
    {
        /// <summary>Mesh coverage for FillRect, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshFillRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for FillRoundedRect, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshFillRoundedRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRoundedRectCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for FillCircle, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshFillCircle : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for DrawLine, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshDrawLine : PipelineDeclaration2D, IPipelineDeclaration2D<DrawLineCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for FillPath, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshFillPath : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for StrokePath, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshStrokePath : PipelineDeclaration2D, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for DrawImage, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshDrawImage : PipelineDeclaration2D, IPipelineDeclaration2D<DrawImageCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for DrawCoverageMask, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshDrawCoverageMask : PipelineDeclaration2D, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for DrawDistanceField, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshDrawDistanceField : PipelineDeclaration2D, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }

        /// <summary>Mesh coverage for DrawPaintGraph, with explicit bounded configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class MeshDrawPaintGraph : PipelineDeclaration2D, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        {
            /// <summary>Gets the immutable admission and resource bounds.</summary>
            [ComposeParameter]
            public required MeshCoverageOptions2D Options { get; init; }

            /// <inheritdoc/>
            public override Type ProcessorType => typeof(MeshCoverageProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context)
            {
                Options.Validate();
                return new MeshCoverageProcessor2D(context.GetSharedService(new MeshCoverageServiceRequest2D(Options)));
            }
        }
    }
}
