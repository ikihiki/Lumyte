using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

public delegate ValueTask<INativeGpuBackend> NativeRenderBackendFactory(GpuRenderRuntimeOptions options, CancellationToken cancellationToken);

public sealed class NativeRenderProvider : IGpuRenderProvider
{
    private readonly NativeRenderBackendFactory createBackend;
    private readonly Dictionary<GpuRenderPassId, NativeRenderPassRegistry.Registration> registrations;
    public NativeRenderProvider(string id, NativeRenderBackendFactory createBackend, NativeRenderPassRegistry passes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(createBackend); ArgumentNullException.ThrowIfNull(passes);
        Id = id; this.createBackend = createBackend; registrations = passes.Snapshot();
    }
    public string Id { get; }
    public int ContractVersion => 1;
    public async ValueTask<IGpuRenderRuntime> CreateAsync(GpuRenderRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); cancellationToken.ThrowIfCancellationRequested();
        foreach (GpuRenderPassId required in options.RequiredPasses)
        { if (!registrations.ContainsKey(required)) { throw new NotSupportedException($"Native provider '{Id}' does not implement {required}."); } }
        INativeGpuBackend backend = await createBackend(options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Native backend factory returned null.");
        GpuResourceManager? resources = null;
        List<NativeRenderPassRegistry.IPass> created = [];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            resources = new(backend);
            NativePassServices services = new(backend, resources);
            Dictionary<GpuRenderPassId, NativeRenderPassRegistry.IPass> implementations = [];
            foreach ((GpuRenderPassId id, NativeRenderPassRegistry.Registration registration) in registrations)
            {
                NativeRenderPassRegistry.IPass pass = registration.Create(services);
                created.Add(pass); implementations.Add(id, pass);
            }
            return new NativeRenderRuntime(services, registrations, implementations);
        }
        catch (Exception primary)
        {
            List<Exception> errors = [];
            foreach (NativeRenderPassRegistry.IPass pass in Enumerable.Reverse(created))
            { try { await pass.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); } }
            if (resources is not null)
            { try { await resources.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); } }
            if (errors.Count == 0) { backend.Dispose(); throw; }
            errors.Insert(0, primary);
            throw new AggregateException("Native creation and cleanup failed; unresolved backend ownership is retained.", errors);
        }
    }
}
