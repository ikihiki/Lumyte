using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Owns compilation and failed-construction cleanup independently of passive configuration.</summary>
/// <remarks>Adopts passive configuration without allocating processors.</remarks>
/// <param name="declaration">The eleven-slot declaration.</param>
public sealed class RendererBuildHost2D(DrawingCompose.Definitions.Renderer declaration) : IDisposable
{
    private readonly object _gate = new();
    private readonly List<BackendCompileScope2D> _pending = [];
    private CommandRoute2D[]? _frozen;
    private IGraphicDevice? _device;
    private DrawingResources2D? _resourceSnapshot;
    private bool _disposed;

    /// <summary>Gets the passive configuration.</summary>
    public DrawingCompose.Definitions.Renderer Declaration { get; } = declaration ?? throw new ArgumentNullException(nameof(declaration));

    /// <summary>Freezes and describes the same routes used by BuildRenderer, without allocating backends.</summary>
    /// <param name="device">The borrowed, generation-specific device.</param>
    /// <param name="resources">The explicit resource snapshot; inspection never transfers its owner.</param>
    /// <returns>An independent immutable route view.</returns>
    public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow(IGraphicDevice device, DrawingResources2D resources)
    {
        lock (_gate)
        {
            return Array.AsReadOnly(Freeze(device, resources).Select(Declaration.Describe).ToArray());
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
            var scope = new BackendCompileScope2D(device, resourceOwner, PipelineDeclaration2D.ValidateGraph(routes.Select(route => route.Declaration)));
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
                var renderer = new Renderer2D(device, table, resources, resourceOwner, validateResources, Declaration.Retention) { ArgumentTableMode = Declaration.ArgumentTables };
                scope.Commit();
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

        ArgumentNullException.ThrowIfNull(Declaration.Retention);
        Declaration.Retention.Validate();
        if (!Enum.IsDefined(Declaration.ArgumentTables))
        {
            throw new ArgumentOutOfRangeException(nameof(Declaration.ArgumentTables));
        }

        CommandRoute2D[] routes = Declaration.Freeze();

        _device = device;
        _resourceSnapshot = resources;
        return _frozen = routes;
    }
}
