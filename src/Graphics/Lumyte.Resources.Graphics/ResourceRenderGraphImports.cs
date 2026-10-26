using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics;

/// <summary>Acquires exact resource generations and transfers their ownership to one compiled graph.</summary>
/// <remarks>Serialize imports, compilation and disposal. Dispose the compiled plan after GPU completion.</remarks>
public sealed class ResourceRenderGraphImports : IDisposable
{
    private readonly RenderGraphBuilder _graph;
    private readonly ResourceManager _manager;
    private readonly List<IRenderGraphLifetime> _leases = [];
    private bool _closed;

    internal ResourceRenderGraphImports(RenderGraphBuilder graph, ResourceManager manager)
    {
        _graph = graph;
        _manager = manager;
    }

    /// <summary>Acquires a shader artifact or other pass input and retains its exact generation.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="key">The logical resource.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>The borrowed output, retained until compiled-plan disposal or abandoned-scope disposal.</returns>
    public async ValueTask<T> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        using ResourceReference<T> reference = await _manager.AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        return Retain(reference.Acquire());
    }

    /// <summary>Acquires a preloaded handle from one consistent publication snapshot without I/O.</summary>
    /// <typeparam name="T">The resource output type.</typeparam>
    /// <param name="snapshot">The borrowed publication view; issued leases outlive this snapshot.</param>
    /// <param name="handle">The preloaded resource handle.</param>
    /// <returns>The retained output.</returns>
    public T Acquire<T>(ResourceSnapshot snapshot, ResourceHandle<T> handle)
        where T : notnull
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!snapshot.TryAcquire(handle, out ResourceLease<T>? lease))
        {
            throw new InvalidOperationException("The captured publication does not contain a usable resource for this handle.");
        }

        return Retain(lease!);
    }

    /// <summary>Imports a texture from the manager, retaining its content generation.</summary>
    /// <param name="key">The texture resource key.</param>
    /// <param name="initialState">The actual preceding texture state.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>The graph-local handle.</returns>
    public async ValueTask<RenderGraphTexture> ImportTextureAsync(ResourceKey<IGraphicsTexture> key, TextureState initialState, BarrierScope initialScope, CancellationToken cancellationToken = default)
        => _graph.ImportTexture(await AcquireAsync(key, cancellationToken).ConfigureAwait(false), initialState, initialScope);

    /// <summary>Imports a typed buffer from the manager.</summary>
    /// <typeparam name="T">The unmanaged buffer element.</typeparam>
    /// <param name="key">The buffer resource key.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>The graph-local handle.</returns>
    public async ValueTask<RenderGraphBuffer<T>> ImportBufferAsync<T>(ResourceKey<IGraphicsBuffer<T>> key, BarrierScope initialScope, CancellationToken cancellationToken = default)
        where T : unmanaged => _graph.ImportBuffer(await AcquireAsync(key, cancellationToken).ConfigureAwait(false), initialScope);

    /// <summary>Imports shader data from the manager.</summary>
    /// <typeparam name="T">The application shader data structure.</typeparam>
    /// <param name="key">The shader data resource key.</param>
    /// <param name="initialScope">The preceding accesses.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>The graph-local handle.</returns>
    public async ValueTask<RenderGraphShaderDataBuffer<T>> ImportShaderDataBufferAsync<T>(ResourceKey<IGraphicsShaderDataBuffer<T>> key, BarrierScope initialScope, CancellationToken cancellationToken = default)
        where T : struct, IShaderData => _graph.ImportShaderDataBuffer(await AcquireAsync(key, cancellationToken).ConfigureAwait(false), initialScope);

    /// <summary>Compiles the graph and transfers all acquired ownership to the resulting plan.</summary>
    /// <param name="frameId">The diagnostic frame identifier.</param>
    /// <param name="capture">Optional graph capture configuration.</param>
    /// <returns>The plan, which must remain alive until GPU completion.</returns>
    public CompiledRenderGraph Compile(long frameId = 0, RenderGraphCaptureOptions? capture = null)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        CompiledRenderGraph plan = _graph.Compile(frameId, capture);
        try
        {
            while (_leases.Count != 0)
            {
                plan.Retain(_leases[^1]);
                _leases.RemoveAt(_leases.Count - 1);
            }

            _closed = true;
            return plan;
        }
        catch
        {
            plan.Dispose();
            throw;
        }
    }

    /// <summary>Returns ownership for an abandoned import scope; transferred ownership belongs to the plan.</summary>
    public void Dispose()
    {
        while (_leases.Count != 0)
        {
            _leases[^1].Dispose();
            _leases.RemoveAt(_leases.Count - 1);
        }

        _closed = true;
    }

    private T Retain<T>(ResourceLease<T> lease)
        where T : notnull
    {
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            T value = lease.Value;
            if (value is GraphicsShaderResource shader && _graph.Device is { } device && !ReferenceEquals(device, shader.Device))
            {
                throw new InvalidOperationException("The shader and graph must use the same physical device.");
            }

            _leases.Add(new Lifetime<T>(lease));
            return value;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private sealed class Lifetime<T>(ResourceLease<T> lease) : IRenderGraphLifetime
        where T : notnull
    {
        public void Validate() => _ = lease.Value;

        public void Dispose() => lease.Dispose();
    }
}
