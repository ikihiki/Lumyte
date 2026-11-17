using Lumyte.Composition;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Generates construction-only factories for the current eleven command backends.</summary>
public static partial class DrawingCompose
{
    /// <summary>Contains the public Composition declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Compiles all roots together into one resource coordinator and ordered renderer.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class Renderer : IDisposable
        {
            private readonly object _gate = new();
            private readonly List<BackendCompileScope2D> _pending = [];
            private CommandRoute2D[]? _frozen;
            private IGraphicDevice? _device;
            private DrawingResources2D? _resourceSnapshot;
            private bool _disposed;

            /// <summary>Gets explicitly initialized routes; no default graph is filled.</summary>
            [ComposeContent]
            public RouteContent2D Children { get; } = new();

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
                    var scope = new BackendCompileScope2D(device);
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

                CommandRoute2D[] routes = Children.Snapshot();
                if (routes.Length != 11 || routes.Select(r => r.Kind).Distinct().Count() != 11)
                {
                    throw new ArgumentException("Exactly one explicit route for each of the eleven command kinds is required.");
                }

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
        }

        /// <summary>Binds the FillRect command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FillRect : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IFillRectRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.FillRect;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the FillRoundedRect command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FillRoundedRect : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IFillRoundedRectRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.FillRoundedRect;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the FillCircle command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FillCircle : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IFillCircleRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.FillCircle;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawLine command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawLine : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawLineRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawLine;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the FillPath command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class FillPath : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IFillPathRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.FillPath;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the StrokePath command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class StrokePath : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IStrokePathRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.StrokePath;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawImage command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawImage : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawImageRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawImage;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawCoverageMask command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawCoverageMask : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawCoverageMaskRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawCoverageMask;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawDistanceField command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawDistanceField : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawDistanceFieldRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawDistanceField;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawMesh command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawMesh : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawMeshRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawMesh;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }

        /// <summary>Binds the DrawPaintGraph command directly to its original typed backend.</summary>
        [Composable(Factory = "DrawingCompose")]
        public partial class DrawPaintGraph : CommandRoute2D
        {
            /// <summary>Gets the explicitly selected backend binding.</summary>
            [ComposeParameter]
            public required BackendBinding2D<IDrawPaintGraphRenderer2D> Backend { get; init; }

            internal override DrawKind2D Kind => DrawKind2D.DrawPaintGraph;

            internal override IBackendBinding2D Binding => Backend ?? throw new ArgumentException("An explicit backend is required.");

            internal override object Compile(BackendCompileScope2D scope) => (Backend ?? throw new ArgumentException("An explicit backend is required.")).Resolve(scope);
        }
    }
}
