using Lumyte.Composition;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Generates construction-only factories for the current eleven command backends.</summary>
public static partial class DrawingCompose
{
    /// <summary>Contains the public Composition declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Compiles all named slots together into one resource coordinator and ordered renderer.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class Renderer : IDisposable
        {
            private readonly object _gate = new();
            private readonly List<BackendCompileScope2D> _pending = [];
            private readonly CommandRoute2D?[] _slots = new CommandRoute2D?[11];
            private CommandRoute2D[]? _frozen;
            private IGraphicDevice? _device;
            private DrawingResources2D? _resourceSnapshot;
            private bool _disposed;

            /// <summary>Gets the required explicit retained-resource bounds.</summary>
            [ComposeParameter]
            public required DrawingRetentionOptions2D Retention { get; init; }

            /// <summary>Gets the required argument-table policy.</summary>
            [ComposeParameter]
            public required DrawingArgumentTableMode ArgumentTables { get; init; }

            /// <summary>Freezes and describes the same routes used by BuildRenderer, without allocating backends.</summary>
            /// <param name="device">The borrowed, generation-specific device.</param>
            /// <param name="resources">The explicit resource snapshot; inspection never transfers its owner.</param>
            /// <returns>An independent immutable route view.</returns>
            public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow(IGraphicDevice device, DrawingResources2D resources)
            {
                lock (_gate)
                {
                    return Array.AsReadOnly(Freeze(device, resources).Select(Describe).ToArray());
                }
            }

            /// <summary>Builds one renderer, resolving graph-local identities once; each call has independent owned state.</summary>
            /// <param name="device">The borrowed device fixed by first inspection or build.</param>
            /// <param name="resources">Explicit borrowed shader pipeline and optional sampler; no preset is inserted.</param>
            /// <param name="resourceOwner">Transferred only on successful construction.</param>
            /// <param name="validateResources">The existing external resource guard.</param>
            /// <returns>The common direct and retained renderer.</returns>
            public IRenderer2D BuildRenderer(IGraphicDevice device, DrawingResources2D resources, IDisposable? resourceOwner, Action? validateResources)
            {
                lock (_gate)
                {
                    CommandRoute2D[] routes = Freeze(device, resources);
                    var scope = new BackendCompileScope2D(device, resourceOwner);
                    try
                    {
                        object[] leaves = new object[11];
                        foreach (CommandRoute2D route in routes)
                        {
                            leaves[(int)route.Kind] = route.Compile(scope);
                        }

                        var table = new DrawingCommandRenderers2D(
                            (IFillRectRenderer2D)leaves[0],
                            (IFillRoundedRectRenderer2D)leaves[1],
                            (IFillCircleRenderer2D)leaves[2],
                            (IDrawLineRenderer2D)leaves[3],
                            (IFillPathRenderer2D)leaves[4],
                            (IStrokePathRenderer2D)leaves[5],
                            (IDrawImageRenderer2D)leaves[6],
                            (IDrawCoverageMaskRenderer2D)leaves[7],
                            (IDrawDistanceFieldRenderer2D)leaves[8],
                            (IDrawMeshRenderer2D)leaves[9],
                            (IDrawPaintGraphRenderer2D)leaves[10]);
                        var renderer = new Renderer2D(device, table, resources, resourceOwner, validateResources, Retention) { ArgumentTableMode = ArgumentTables };
                        renderer.AttachCompositionOwner(scope);
                        return renderer;
                    }
                    catch (Exception error)
                    {
                        try
                        {
                            scope.Dispose();
                        }
                        catch (Exception cleanup)
                        {
                            _pending.Add(scope);
                            throw new AggregateException(error, cleanup);
                        }

                        throw;
                    }
                }
            }

            /// <summary>Retries unpublished construction cleanup; already built renderers retain their own scopes.</summary>
            public void Dispose()
            {
                lock (_gate)
                {
                    _disposed = true;
                    foreach (BackendCompileScope2D pending in _pending.ToArray())
                    {
                        pending.Dispose();
                        _pending.Remove(pending);
                    }
                }
            }

            [ComposeSlot]
            private static void FillRect(Renderer target, IReadOnlyList<BackendBinding2D<IFillRectRenderer2D>> children) => target.SetSlot(DrawKind2D.FillRect, children);

            [ComposeSlot]
            private static void FillRoundedRect(Renderer target, IReadOnlyList<BackendBinding2D<IFillRoundedRectRenderer2D>> children) => target.SetSlot(DrawKind2D.FillRoundedRect, children);

            [ComposeSlot]
            private static void FillCircle(Renderer target, IReadOnlyList<BackendBinding2D<IFillCircleRenderer2D>> children) => target.SetSlot(DrawKind2D.FillCircle, children);

            [ComposeSlot]
            private static void DrawLine(Renderer target, IReadOnlyList<BackendBinding2D<IDrawLineRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawLine, children);

            [ComposeSlot]
            private static void FillPath(Renderer target, IReadOnlyList<BackendBinding2D<IFillPathRenderer2D>> children) => target.SetSlot(DrawKind2D.FillPath, children);

            [ComposeSlot]
            private static void StrokePath(Renderer target, IReadOnlyList<BackendBinding2D<IStrokePathRenderer2D>> children) => target.SetSlot(DrawKind2D.StrokePath, children);

            [ComposeSlot]
            private static void DrawImage(Renderer target, IReadOnlyList<BackendBinding2D<IDrawImageRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawImage, children);

            [ComposeSlot]
            private static void DrawCoverageMask(Renderer target, IReadOnlyList<BackendBinding2D<IDrawCoverageMaskRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawCoverageMask, children);

            [ComposeSlot]
            private static void DrawDistanceField(Renderer target, IReadOnlyList<BackendBinding2D<IDrawDistanceFieldRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawDistanceField, children);

            [ComposeSlot]
            private static void DrawMesh(Renderer target, IReadOnlyList<BackendBinding2D<IDrawMeshRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawMesh, children);

            [ComposeSlot]
            private static void DrawPaintGraph(Renderer target, IReadOnlyList<BackendBinding2D<IDrawPaintGraphRenderer2D>> children) => target.SetSlot(DrawKind2D.DrawPaintGraph, children);

            private static CommandRouteDescription2D Describe(CommandRoute2D route) => route.Kind switch
            {
                DrawKind2D.FillRect => new(route.Kind, typeof(FillRectCommand2D), typeof(IFillRectRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillRoundedRect => new(route.Kind, typeof(FillRoundedRectCommand2D), typeof(IFillRoundedRectRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillCircle => new(route.Kind, typeof(FillCircleCommand2D), typeof(IFillCircleRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawLine => new(route.Kind, typeof(DrawLineCommand2D), typeof(IDrawLineRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.FillPath => new(route.Kind, typeof(FillPathCommand2D), typeof(IFillPathRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.StrokePath => new(route.Kind, typeof(StrokePathCommand2D), typeof(IStrokePathRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawImage => new(route.Kind, typeof(DrawImageCommand2D), typeof(IDrawImageRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawCoverageMask => new(route.Kind, typeof(DrawCoverageMaskCommand2D), typeof(IDrawCoverageMaskRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawDistanceField => new(route.Kind, typeof(DrawDistanceFieldCommand2D), typeof(IDrawDistanceFieldRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawMesh => new(route.Kind, typeof(DrawMeshCommand2D), typeof(IDrawMeshRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                DrawKind2D.DrawPaintGraph => new(route.Kind, typeof(DrawPaintGraphCommand2D), typeof(IDrawPaintGraphRenderer2D), route.Binding.SharedName, route.Binding.ConcreteType, route.Binding.ChoiceBackends, "Typed consecutive CPU preparation; shared argument plan and resource state; builtin record encoding/packing/upload followed by fused fragment shape+paint, or explicit custom PrepareAsync."),
                _ => throw new ArgumentException("Unhandled route."),
            };

            private CommandRoute2D[] Freeze(IGraphicDevice device, DrawingResources2D resources)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ArgumentNullException.ThrowIfNull(device);
                ArgumentNullException.ThrowIfNull(resources);
                if (!ReferenceEquals(resources.Device, device))
                {
                    throw new ArgumentException("Resources belong to a different device.", nameof(resources));
                }

                if (device.Status != GraphicsDeviceStatus.Ready)
                {
                    throw new InvalidOperationException("Compilation requires a ready device generation.");
                }

                if (_resourceSnapshot is not null && !ReferenceEquals(_resourceSnapshot, resources))
                {
                    throw new InvalidOperationException("A frozen definition requires its original resource snapshot.");
                }

                foreach (BackendCompileScope2D pending in _pending.ToArray())
                {
                    pending.Dispose();
                    _pending.Remove(pending);
                }

                if (_device is not null && !ReferenceEquals(_device, device))
                {
                    throw new InvalidOperationException("A frozen definition belongs to one device generation.");
                }

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
                    throw new ArgumentException("Exactly one explicit route for each of the eleven command kinds is required.");
                }

                CommandRoute2D[] routes = _slots.Select(route => route!).ToArray();
                var shares = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (CommandRoute2D route in routes)
                {
                    IBackendBinding2D binding = route.Binding;
                    if (binding.SharedName is string name)
                    {
                        if (shares.TryGetValue(name, out object? identity) && !ReferenceEquals(identity, binding.SharedIdentity))
                        {
                            throw new ArgumentException("Conflicting graph-local shared identities.");
                        }

                        shares[name] = binding.SharedIdentity!;
                    }
                }

                _device = device;
                _resourceSnapshot = resources;
                return _frozen = routes;
            }

            private void SetSlot<T>(DrawKind2D kind, IReadOnlyList<BackendBinding2D<T>> children)
                where T : class
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_frozen is not null)
                    {
                        throw new InvalidOperationException("Named slots are frozen.");
                    }

                    ArgumentNullException.ThrowIfNull(children);
                    if (children.Count > 1 || (children.Count == 1 && children[0] is null))
                    {
                        throw new ArgumentException("A named slot requires exactly one explicit non-null binding, or no children to clear it.", nameof(children));
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

                    BackendBinding2D<T> binding = children[0];
                    _slots[index] = new(kind, binding, scope => binding.Resolve(scope));
                }
            }
        }
    }
}
