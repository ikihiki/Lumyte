using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

public delegate INativeRenderPass<TRequest, TResult> NativeRenderPassFactory<TRequest, TResult>(NativePassServices services);

public interface INativeRenderPass<TRequest, TResult> : IAsyncDisposable
{
    ValueTask BuildAsync(NativePassBuildContext context, TRequest request, TResult result, CancellationToken cancellationToken = default);
}

public sealed class NativePassServices
{
    internal NativePassServices(INativeGpuBackend backend, GpuResourceManager resources)
    { Backend = backend; Resources = resources; ShaderLoader = new(backend); }
    public INativeGpuBackend Backend { get; }
    public GpuResourceManager Resources { get; }
    public NativeShaderLoader ShaderLoader { get; }
}

/// <summary>Bootstrap definitions. Providers snapshot these registrations before creating a device.</summary>
public sealed class NativeRenderPassRegistry
{
    private readonly Dictionary<GpuRenderPassId, Registration> registrations = [];
    public void Register<TRequest, TResult>(IGpuRenderPassContract<TRequest, TResult> contract,
        NativeRenderPassFactory<TRequest, TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(contract); ArgumentNullException.ThrowIfNull(factory);
        var id = new GpuRenderPassId(contract.Id, contract.Version);
        if (!registrations.TryAdd(id, new(typeof(TRequest), typeof(TResult), services => new Pass<TRequest, TResult>(factory(services)
            ?? throw new InvalidOperationException("The Native pass factory returned null.")))))
        { throw new ArgumentException($"Pass {id} already has a Native implementation.", nameof(contract)); }
    }
    internal Dictionary<GpuRenderPassId, Registration> Snapshot() => new(registrations);
    internal sealed record Registration(Type RequestType, Type ResultType, Func<NativePassServices, IPass> Create);
    internal interface IPass : IAsyncDisposable
    {
        ValueTask BuildAsync(NativePassBuildContext context, object request, object result, CancellationToken cancellationToken);
    }
    private sealed class Pass<TRequest, TResult>(INativeRenderPass<TRequest, TResult> implementation) : IPass
    {
        public ValueTask BuildAsync(NativePassBuildContext context, object request, object result, CancellationToken cancellationToken)
            => implementation.BuildAsync(context, (TRequest)request, (TResult)result, cancellationToken);
        public ValueTask DisposeAsync() => implementation.DisposeAsync();
    }
}
