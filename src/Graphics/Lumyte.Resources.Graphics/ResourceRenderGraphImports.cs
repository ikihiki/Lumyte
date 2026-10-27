using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Resources.Graphics;

/// <summary>A borrowed graph extension that acquires exact resource generations for the parent graph.</summary>
/// <remarks>Serialize graph construction and imports. The graph and its compiled plan own acquired lifetimes.</remarks>
public sealed class ResourceRenderGraphImports
{
    private readonly RenderGraphBuilder _graph;
    private readonly ResourceManager _manager;
    private ResourceSnapshot? _snapshot;
    private bool _acquired;

    internal ResourceRenderGraphImports(RenderGraphBuilder graph, ResourceManager manager)
    {
        _graph = graph;
        _manager = manager;
    }

    /// <summary>Gets the parent graph owning acquired resources and compilation.</summary>
    public RenderGraphBuilder Graph => _graph;

    internal ResourceManager Manager => _manager;

    /// <summary>Selects one borrowed publication view for subsequent acquisitions on this graph.</summary>
    /// <param name="snapshot">The caller-owned snapshot, kept alive until graph construction finishes.</param>
    /// <returns>This shared import context.</returns>
    /// <remarks>Select the view once before any acquisition. Existing leases outlive the snapshot.</remarks>
    public ResourceRenderGraphImports UseSnapshot(ResourceSnapshot snapshot)
    {
        EnsureBuilding();
        if (_acquired || _snapshot is not null)
        {
            throw new InvalidOperationException("Select the frame snapshot once before acquiring resources.");
        }

        _snapshot = snapshot;
        return this;
    }

    /// <summary>Pins a preloaded reference without I/O, using the selected frame snapshot when present.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="reference">A preloaded reference owned by this graph's resource manager.</param>
    /// <returns>The borrowed value, retained by the graph and its compiled plan.</returns>
    public T Acquire<T>(ResourceReference<T> reference)
        where T : notnull
    {
        BeginAcquisition();
        ValidateManager(reference.Handle);
        return Retain(reference.Pin(_snapshot));
    }

    /// <summary>Acquires a shader artifact or other pass input and retains its exact generation.</summary>
    /// <typeparam name="T">The registered output type.</typeparam>
    /// <param name="key">The logical resource.</param>
    /// <param name="cancellationToken">Cancels acquisition.</param>
    /// <returns>The borrowed output, retained until compiled-plan disposal or abandoned-graph disposal.</returns>
    public async ValueTask<T> AcquireAsync<T>(ResourceKey<T> key, CancellationToken cancellationToken = default)
        where T : notnull
    {
        BeginAcquisition();
        using ResourceReference<T> reference = await _manager.RetainAsync(key, cancellationToken).ConfigureAwait(false);
        EnsureBuilding();
        return Retain(reference.Pin(_snapshot));
    }

    /// <summary>Acquires a preloaded handle from one consistent publication snapshot without I/O.</summary>
    /// <typeparam name="T">The resource output type.</typeparam>
    /// <param name="snapshot">The borrowed publication view; issued leases outlive this snapshot.</param>
    /// <param name="handle">The preloaded resource handle.</param>
    /// <returns>The retained output.</returns>
    public T Acquire<T>(ResourceSnapshot snapshot, ResourceHandle<T> handle)
        where T : notnull
    {
        BeginAcquisition();
        ValidateManager(handle);
        if (_snapshot is not null && !ReferenceEquals(_snapshot, snapshot))
        {
            throw new ArgumentException("Use the publication view selected for this graph.", nameof(snapshot));
        }

        if (!snapshot.TryPin(handle, out ResourceLease<T>? lease))
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

    private void EnsureBuilding() => ObjectDisposedException.ThrowIf(!_graph.IsBuilding, this);

    private void BeginAcquisition()
    {
        EnsureBuilding();
        _acquired = true;
    }

    private void ValidateManager<T>(ResourceHandle<T> handle)
        where T : notnull
    {
        if (handle.ManagerId != _manager.ManagerId)
        {
            throw new ArgumentException("The reference must belong to the graph's resource manager.", nameof(handle));
        }
    }

    private T Retain<T>(ResourceLease<T> lease)
        where T : notnull
    {
        try
        {
            EnsureBuilding();
            T value = lease.Value;
            if (value is GraphicsShaderResource shader && _graph.Device is { } device && !ReferenceEquals(device, shader.Device))
            {
                throw new InvalidOperationException("The shader and graph must use the same physical device.");
            }

            _graph.Retain(new Lifetime<T>(lease));
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
