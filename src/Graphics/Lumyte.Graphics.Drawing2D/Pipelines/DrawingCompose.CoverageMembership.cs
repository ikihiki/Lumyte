using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive coverage membership algorithm factories.</summary>
public static partial class DrawingCompose
{
    /// <summary>Passive coverage membership algorithm declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Passive coverage membership FillRect algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipFillRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership FillRoundedRect algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipFillRoundedRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRoundedRectCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership FillCircle algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipFillCircle : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawLine algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawLine : PipelineDeclaration2D, IPipelineDeclaration2D<DrawLineCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership FillPath algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipFillPath : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership StrokePath algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipStrokePath : PipelineDeclaration2D, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawImage algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawImage : PipelineDeclaration2D, IPipelineDeclaration2D<DrawImageCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawCoverageMask algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawCoverageMask : PipelineDeclaration2D, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawDistanceField algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawDistanceField : PipelineDeclaration2D, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawMesh algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawMesh : PipelineDeclaration2D, IPipelineDeclaration2D<DrawMeshCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }

        /// <summary>Passive coverage membership DrawPaintGraph algorithm configuration.</summary>
        [Composable(Factory = "DrawingCompose")]
        public sealed partial class CoverageMembershipDrawPaintGraph : PipelineDeclaration2D, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        {
            /// <inheritdoc/>
            public override Type ProcessorType => typeof(CoverageMembershipProcessor2D);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => new CoverageMembershipProcessor2D(context.GetSharedService(new CoverageMembershipServiceRequest2D()));
        }
    }
}
