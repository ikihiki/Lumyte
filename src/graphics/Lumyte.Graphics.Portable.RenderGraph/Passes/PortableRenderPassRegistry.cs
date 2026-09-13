using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.RenderGraph;

public delegate ValueTask<IPortableGpuBackend> PortableRenderBackendFactory(GpuRenderRuntimeOptions options, CancellationToken cancellationToken);
public delegate IPortableRenderPass<TRequest, TResult> PortableRenderPassFactory<TRequest, TResult>(PortablePassServices services);

public interface IPortableRenderPass<TRequest, TResult> : IAsyncDisposable
{
    ValueTask BuildAsync(PortablePassBuildContext context, TRequest request, TResult result, CancellationToken cancellationToken = default);
}

/// <summary>Borrowed runtime services, without a service locator or asset I/O.</summary>
public sealed class PortablePassServices
{
    internal PortablePassServices(IPortableGpuBackend backend, GpuResourceManager resources)
    { Backend = backend; Resources = resources; ShaderLoader = new(backend); }
    public IPortableGpuBackend Backend { get; }
    public GpuResourceManager Resources { get; }
    public PortableShaderLoader ShaderLoader { get; }
}

/// <summary>Startup-only typed pass registrations. A provider snapshots this collection.</summary>
public sealed class PortableRenderPassRegistry
{
    private readonly Dictionary<GpuRenderPassId, PassRegistration> registrations = [];
    public void Register<TRequest, TResult>(IGpuRenderPassContract<TRequest, TResult> contract,
        PortableRenderPassFactory<TRequest, TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(contract); ArgumentNullException.ThrowIfNull(factory);
        var key = new GpuRenderPassId(contract.Id, contract.Version);
        if (!registrations.TryAdd(key, new PassRegistration<TRequest, TResult>(factory)))
        { throw new InvalidOperationException($"A Portable implementation for '{key}' is already registered."); }
    }
    internal Dictionary<GpuRenderPassId, PassRegistration> Snapshot() => new(registrations);
}

internal abstract class PassRegistration
{
    internal abstract PassInstance Create(PortablePassServices services);
    internal abstract bool Matches(GpuRenderGraphPass pass);
}
internal sealed class PassRegistration<TRequest, TResult>(PortableRenderPassFactory<TRequest, TResult> factory) : PassRegistration
{
    internal override PassInstance Create(PortablePassServices services)
        => new TypedPassInstance<TRequest, TResult>(factory(services) ?? throw new InvalidOperationException("The pass factory returned null."));
    internal override bool Matches(GpuRenderGraphPass pass) => pass.RequestType == typeof(TRequest) && pass.ResultType == typeof(TResult);
}
internal abstract class PassInstance : IAsyncDisposable
{
    internal abstract ValueTask BuildAsync(PortablePassBuildContext context, GpuRenderGraphPass pass, CancellationToken cancellationToken);
    public abstract ValueTask DisposeAsync();
}
internal sealed class TypedPassInstance<TRequest, TResult>(IPortableRenderPass<TRequest, TResult> pass) : PassInstance
{
    internal override ValueTask BuildAsync(PortablePassBuildContext context, GpuRenderGraphPass declaration, CancellationToken cancellationToken)
        => pass.BuildAsync(context, (TRequest)declaration.Request!, (TResult)declaration.Result!, cancellationToken);
    public override ValueTask DisposeAsync() => pass.DisposeAsync();
}
