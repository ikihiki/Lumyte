using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive standard algorithm factories.</summary>
public static partial class DrawingCompose
{
    /// <summary>Passive standard algorithm declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Passive standard FillRect algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardFillRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultFillRectRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultFillRectRenderer2D();
        }

        /// <summary>Passive standard FillRoundedRect algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardFillRoundedRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRoundedRectCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultFillRoundedRectRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultFillRoundedRectRenderer2D();
        }

        /// <summary>Passive standard FillCircle algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardFillCircle : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultFillCircleRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultFillCircleRenderer2D();
        }

        /// <summary>Passive standard DrawLine algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawLine : PipelineDeclaration2D, IPipelineDeclaration2D<DrawLineCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawLineRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawLineRenderer2D();
        }

        /// <summary>Passive standard FillPath algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardFillPath : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultFillPathRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultFillPathRenderer2D();
        }

        /// <summary>Passive standard StrokePath algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardStrokePath : PipelineDeclaration2D, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultStrokePathRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultStrokePathRenderer2D();
        }

        /// <summary>Passive standard DrawImage algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawImage : PipelineDeclaration2D, IPipelineDeclaration2D<DrawImageCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawImageRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawImageRenderer2D();
        }

        /// <summary>Passive standard DrawCoverageMask algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawCoverageMask : PipelineDeclaration2D, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawCoverageMaskRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawCoverageMaskRenderer2D();
        }

        /// <summary>Passive standard DrawDistanceField algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawDistanceField : PipelineDeclaration2D, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawDistanceFieldRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawDistanceFieldRenderer2D();
        }

        /// <summary>Passive standard DrawMesh algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawMesh : PipelineDeclaration2D, IPipelineDeclaration2D<DrawMeshCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawMeshRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawMeshRenderer2D();
        }

        /// <summary>Passive standard DrawPaintGraph algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class StandardDrawPaintGraph : PipelineDeclaration2D, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(DefaultDrawPaintGraphRenderer2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new DefaultDrawPaintGraphRenderer2D();
        }
    }
}
