using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Factories for passive standard and choice configurations.</summary>
public static partial class DrawingCompose
{
    /// <summary>Passive command algorithm declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Passive ordered choice restricted to FillRect commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedFillRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
        {
            private PipelineCandidate2D<FillRectCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<FillRectCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.FillRect);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.FillRect(_candidates.Select(candidate =>
                new CommandCandidate2D<FillRectCommand2D, IFillRectRenderer2D>(candidate.Supports, context.GetProcessor<IFillRectRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to FillRoundedRect commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedFillRoundedRect : PipelineDeclaration2D, IPipelineDeclaration2D<FillRoundedRectCommand2D>
        {
            private PipelineCandidate2D<FillRoundedRectCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<FillRoundedRectCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.FillRoundedRect);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.FillRoundedRect(_candidates.Select(candidate =>
                new CommandCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>(candidate.Supports, context.GetProcessor<IFillRoundedRectRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to FillCircle commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedFillCircle : PipelineDeclaration2D, IPipelineDeclaration2D<FillCircleCommand2D>
        {
            private PipelineCandidate2D<FillCircleCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<FillCircleCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.FillCircle);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.FillCircle(_candidates.Select(candidate =>
                new CommandCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>(candidate.Supports, context.GetProcessor<IFillCircleRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawLine commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawLine : PipelineDeclaration2D, IPipelineDeclaration2D<DrawLineCommand2D>
        {
            private PipelineCandidate2D<DrawLineCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawLineCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawLine);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawLine(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>(candidate.Supports, context.GetProcessor<IDrawLineRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to FillPath commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedFillPath : PipelineDeclaration2D, IPipelineDeclaration2D<FillPathCommand2D>
        {
            private PipelineCandidate2D<FillPathCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<FillPathCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.FillPath);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.FillPath(_candidates.Select(candidate =>
                new CommandCandidate2D<FillPathCommand2D, IFillPathRenderer2D>(candidate.Supports, context.GetProcessor<IFillPathRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to StrokePath commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedStrokePath : PipelineDeclaration2D, IPipelineDeclaration2D<StrokePathCommand2D>
        {
            private PipelineCandidate2D<StrokePathCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<StrokePathCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.StrokePath);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.StrokePath(_candidates.Select(candidate =>
                new CommandCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>(candidate.Supports, context.GetProcessor<IStrokePathRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawImage commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawImage : PipelineDeclaration2D, IPipelineDeclaration2D<DrawImageCommand2D>
        {
            private PipelineCandidate2D<DrawImageCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawImageCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawImage);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawImage(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>(candidate.Supports, context.GetProcessor<IDrawImageRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawCoverageMask commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawCoverageMask : PipelineDeclaration2D, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>
        {
            private PipelineCandidate2D<DrawCoverageMaskCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawCoverageMaskCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawCoverageMask);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawCoverageMask(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>(candidate.Supports, context.GetProcessor<IDrawCoverageMaskRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawDistanceField commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawDistanceField : PipelineDeclaration2D, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>
        {
            private PipelineCandidate2D<DrawDistanceFieldCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawDistanceFieldCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawDistanceField);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawDistanceField(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>(candidate.Supports, context.GetProcessor<IDrawDistanceFieldRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawMesh commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawMesh : PipelineDeclaration2D, IPipelineDeclaration2D<DrawMeshCommand2D>
        {
            private PipelineCandidate2D<DrawMeshCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawMeshCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawMesh);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawMesh(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>(candidate.Supports, context.GetProcessor<IDrawMeshRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }

        /// <summary>Passive ordered choice restricted to DrawPaintGraph commands.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FirstSupportedDrawPaintGraph : PipelineDeclaration2D, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        {
            private PipelineCandidate2D<DrawPaintGraphCommand2D>[] _candidates = [];

            /// <summary>Gets the explicitly ordered immutable candidate snapshot.</summary>
            [ComposeParameter]
            public required IReadOnlyList<PipelineCandidate2D<DrawPaintGraphCommand2D>> Candidates
            {
                get => Array.AsReadOnly(_candidates);
                init
                {
                    ArgumentNullException.ThrowIfNull(value);
                    if (value.Count == 0 || value.Any(candidate => candidate is null || candidate.Supports is null || candidate.Pipeline is null))
                    {
                        throw new ArgumentException("A choice requires explicit non-null candidates.", nameof(value));
                    }

                    _candidates = value.ToArray();
                }
            }

            /// <inheritdoc/>
            public override Type ProcessorType => FirstSupported2D.GetProcessorType(DrawKind2D.DrawPaintGraph);

            internal override IReadOnlyList<Type> ChoiceProcessors => Array.AsReadOnly(_candidates.Select(candidate => candidate.Pipeline.Declaration.ProcessorType).ToArray());

            /// <inheritdoc/>
            protected override IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => _candidates.Select(candidate => candidate.Pipeline.Declaration);

            /// <inheritdoc/>
            protected override object CreateElement(PipelineBuildContext2D context) => FirstSupported2D.DrawPaintGraph(_candidates.Select(candidate =>
                new CommandCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>(candidate.Supports, context.GetProcessor<IDrawPaintGraphRenderer2D>(candidate.Pipeline.Declaration))).ToArray());
        }
    }
}
