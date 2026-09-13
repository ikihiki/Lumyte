using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Portable.Hosting;

public static class PortableGraphicsHostExtensions
{
    public static LumyteGraphicsBuilder AddPortableProvider(
        this LumyteGraphicsBuilder builder,
        string id,
        PortableRenderBackendFactory createBackend,
        Action<PortableRenderPassRegistry>? configurePasses = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(createBackend);
        return builder.AddProvider(new ProviderDefinition(id, createBackend, configurePasses));
    }

    public static LumyteGraphicsBuilder AddPortablePasses(
        this LumyteGraphicsBuilder builder,
        Action<IServiceProvider, PortableRenderPassRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddSingleton(new PassRegistration(configure));
        return builder;
    }

    private sealed record PassRegistration(Action<IServiceProvider, PortableRenderPassRegistry> Configure);

    private sealed class ProviderDefinition(
        string id,
        PortableRenderBackendFactory createBackend,
        Action<PortableRenderPassRegistry>? configurePasses) : IGpuRenderProviderDefinition
    {
        public ValueTask<IGpuRenderProvider> CreateAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passes = new PortableRenderPassRegistry();
            configurePasses?.Invoke(passes);
            foreach (PassRegistration registration in services.GetServices<PassRegistration>())
            {
                registration.Configure(services, passes);
            }
            return ValueTask.FromResult<IGpuRenderProvider>(new PortableRenderProvider(id, createBackend, passes));
        }
    }
}
