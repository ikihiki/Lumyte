using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Construction syntax for passive pipeline configuration.</summary>
public static partial class DrawingCompose
{
    /// <summary>Configuration types distinct from their processing instances.</summary>
    public static partial class Definitions
    {
        /// <summary>Explicit command topology and policy; owns no device, processor or native cleanup.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class Renderer
        {
            private readonly object _gate = new();
            private readonly CommandRoute2D?[] _slots = new CommandRoute2D?[11];
            private CommandRoute2D[]? _frozen;

            /// <summary>Gets required resource bounds.</summary>
            [ComposeParameter]
            public required DrawingRetentionOptions2D Retention { get; init; }

            /// <summary>Gets required argument-table policy.</summary>
            [ComposeParameter]
            public required DrawingArgumentTableMode ArgumentTables { get; init; }

            /// <summary>Creates an independent construction and cleanup owner without creating processors.</summary>
            /// <returns>The runtime construction host.</returns>
            public RendererBuildHost2D CreateHost() => new(this);

            /// <summary>Freezes declaration topology without a device or processor allocation.</summary>
            /// <returns>Declared processing metadata.</returns>
            public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow() => Array.AsReadOnly(Freeze().Select(DescribeSnapshot).ToArray());

            internal CommandRoute2D[] Freeze()
            {
                lock (_gate)
                {
                    if (_frozen is not null)
                    {
                        return _frozen;
                    }

                    ArgumentNullException.ThrowIfNull(Retention);
                    Retention.Validate();
                    if (!Enum.IsDefined(ArgumentTables))
                    {
                        throw new ArgumentOutOfRangeException(nameof(ArgumentTables));
                    }

                    if (_slots.Any(route => route is null))
                    {
                        throw new ArgumentException("Exactly eleven explicit declarations are required.");
                    }

                    foreach (CommandRoute2D route in _slots.Select(route => route!))
                    {
                        Type required = route.Kind switch
                        {
                            DrawKind2D.FillRect => typeof(IFillRectRenderer2D),
                            DrawKind2D.FillRoundedRect => typeof(IFillRoundedRectRenderer2D),
                            DrawKind2D.FillCircle => typeof(IFillCircleRenderer2D),
                            DrawKind2D.DrawLine => typeof(IDrawLineRenderer2D),
                            DrawKind2D.FillPath => typeof(IFillPathRenderer2D),
                            DrawKind2D.StrokePath => typeof(IStrokePathRenderer2D),
                            DrawKind2D.DrawImage => typeof(IDrawImageRenderer2D),
                            DrawKind2D.DrawCoverageMask => typeof(IDrawCoverageMaskRenderer2D),
                            DrawKind2D.DrawDistanceField => typeof(IDrawDistanceFieldRenderer2D),
                            DrawKind2D.DrawMesh => typeof(IDrawMeshRenderer2D),
                            DrawKind2D.DrawPaintGraph => typeof(IDrawPaintGraphRenderer2D),
                            _ => throw new ArgumentException("Unhandled command kind."),
                        };
                        if (route.Declarations.Any(declaration => !required.IsAssignableFrom(declaration.ProcessorType)))
                        {
                            throw new ArgumentException("The declared processing type does not implement the slot capability.");
                        }
                    }

                    foreach (CommandRoute2D route in _slots.Select(route => route!))
                    {
                        route.Occurrences = PipelineDeclaration2D.CaptureGraph(route.Declarations, route.Kind.ToString());
                    }

                    return _frozen = _slots.Select(route => route!).ToArray();
                }
            }

            internal CommandRouteDescription2D DescribeSnapshot(CommandRoute2D route) => Describe(route) with
            {
                OccurrencePaths = Array.AsReadOnly(route.Occurrences.SelectMany(occurrence => occurrence.EnumeratePaths()).ToArray()),
                CandidatePaths = Array.AsReadOnly(route.Occurrences.Select(occurrence => occurrence.Path).ToArray()),
                CandidateIdentities = Array.AsReadOnly(route.Declarations.Select(declaration => declaration.Name).ToArray()),
            };

            [ComposeSlot]
            private static void FillRect(Renderer target, IReadOnlyList<IPipelineDeclaration2D<FillRectCommand2D>> children) => target.SetSlot(DrawKind2D.FillRect, children);

            [ComposeSlot]
            private static void FillRoundedRect(Renderer target, IReadOnlyList<IPipelineDeclaration2D<FillRoundedRectCommand2D>> children) => target.SetSlot(DrawKind2D.FillRoundedRect, children);

            [ComposeSlot]
            private static void FillCircle(Renderer target, IReadOnlyList<IPipelineDeclaration2D<FillCircleCommand2D>> children) => target.SetSlot(DrawKind2D.FillCircle, children);

            [ComposeSlot]
            private static void DrawLine(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawLineCommand2D>> children) => target.SetSlot(DrawKind2D.DrawLine, children);

            [ComposeSlot]
            private static void FillPath(Renderer target, IReadOnlyList<IPipelineDeclaration2D<FillPathCommand2D>> children) => target.SetSlot(DrawKind2D.FillPath, children);

            [ComposeSlot]
            private static void StrokePath(Renderer target, IReadOnlyList<IPipelineDeclaration2D<StrokePathCommand2D>> children) => target.SetSlot(DrawKind2D.StrokePath, children);

            [ComposeSlot]
            private static void DrawImage(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawImageCommand2D>> children) => target.SetSlot(DrawKind2D.DrawImage, children);

            [ComposeSlot]
            private static void DrawCoverageMask(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawCoverageMaskCommand2D>> children) => target.SetSlot(DrawKind2D.DrawCoverageMask, children);

            [ComposeSlot]
            private static void DrawDistanceField(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawDistanceFieldCommand2D>> children) => target.SetSlot(DrawKind2D.DrawDistanceField, children);

            [ComposeSlot]
            private static void DrawMesh(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawMeshCommand2D>> children) => target.SetSlot(DrawKind2D.DrawMesh, children);

            [ComposeSlot]
            private static void DrawPaintGraph(Renderer target, IReadOnlyList<IPipelineDeclaration2D<DrawPaintGraphCommand2D>> children) => target.SetSlot(DrawKind2D.DrawPaintGraph, children);

            private CommandRouteDescription2D Describe(CommandRoute2D route) => route.Kind switch
            {
                DrawKind2D.FillRect => new(route.Kind, typeof(FillRectCommand2D), typeof(IFillRectRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillRoundedRect => new(route.Kind, typeof(FillRoundedRectCommand2D), typeof(IFillRoundedRectRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillCircle => new(route.Kind, typeof(FillCircleCommand2D), typeof(IFillCircleRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawLine => new(route.Kind, typeof(DrawLineCommand2D), typeof(IDrawLineRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillPath => new(route.Kind, typeof(FillPathCommand2D), typeof(IFillPathRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.StrokePath => new(route.Kind, typeof(StrokePathCommand2D), typeof(IStrokePathRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawImage => new(route.Kind, typeof(DrawImageCommand2D), typeof(IDrawImageRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawCoverageMask => new(route.Kind, typeof(DrawCoverageMaskCommand2D), typeof(IDrawCoverageMaskRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawDistanceField => new(route.Kind, typeof(DrawDistanceFieldCommand2D), typeof(IDrawDistanceFieldRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawMesh => new(route.Kind, typeof(DrawMeshCommand2D), typeof(IDrawMeshRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawPaintGraph => new(route.Kind, typeof(DrawPaintGraphCommand2D), typeof(IDrawPaintGraphRenderer2D), route.Declarations[0].Name, route.Declarations[0].ProcessorType, Array.AsReadOnly(route.Declarations.Select(declaration => declaration.ProcessorType).ToArray()), "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                _ => throw new ArgumentException("Unhandled route."),
            };

            private void SetSlot<TCommand>(DrawKind2D kind, IReadOnlyList<IPipelineDeclaration2D<TCommand>> children)
                where TCommand : DrawingCommand2D
            {
                lock (_gate)
                {
                    if (_frozen is not null)
                    {
                        throw new InvalidOperationException("Named slots are frozen.");
                    }

                    ArgumentNullException.ThrowIfNull(children);
                    if (children.Any(child => child is null || child.Declaration is null))
                    {
                        throw new ArgumentException("A named slot requires ordered non-null declarations, or no children to clear it.", nameof(children));
                    }

                    int index = (int)kind;
                    if (children.Count == 0)
                    {
                        _slots[index] = null;
                        return;
                    }

                    if (_slots[index] is not null)
                    {
                        throw new ArgumentException("A named slot is already assigned; clear it explicitly before reassignment.", nameof(children));
                    }

                    PipelineDeclaration2D[] declarations = children.Select(child => child.Declaration).ToArray();
                    _slots[index] = new(kind, declarations);
                }
            }
        }
    }
}
