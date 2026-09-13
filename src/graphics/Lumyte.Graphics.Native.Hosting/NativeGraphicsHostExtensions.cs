using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Native.Hosting;

public static class NativeGraphicsHostExtensions
{
    public static LumyteGraphicsBuilder AddNativeProvider(
        this LumyteGraphicsBuilder builder,
        string id,
        NativeRenderBackendFactory createBackend,
        Action<NativeRenderPassRegistry>? configurePasses = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(createBackend);
        return builder.AddProvider(new ProviderDefinition(id, createBackend, configurePasses));
    }

    public static LumyteGraphicsBuilder AddNativePasses(
        this LumyteGraphicsBuilder builder,
        Action<IServiceProvider, NativeRenderPassRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddSingleton(new PassRegistration(configure));
        return builder;
    }

    private sealed record PassRegistration(Action<IServiceProvider, NativeRenderPassRegistry> Configure);

    private sealed class ProviderDefinition(
        string id,
        NativeRenderBackendFactory createBackend,
        Action<NativeRenderPassRegistry>? configurePasses) : IGpuRenderProviderDefinition
    {
        public ValueTask<IGpuRenderProvider> CreateAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passes = new NativeRenderPassRegistry();
            configurePasses?.Invoke(passes);
            foreach (PassRegistration registration in services.GetServices<PassRegistration>())
            {
                registration.Configure(services, passes);
            }
            return ValueTask.FromResult<IGpuRenderProvider>(new NativeRenderProvider(id, createBackend, passes));
        }
    }
}
