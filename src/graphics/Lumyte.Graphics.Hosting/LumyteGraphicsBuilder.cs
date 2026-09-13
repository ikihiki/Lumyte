using Lumyte.Graphics.RenderGraph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lumyte.Graphics.Hosting;

public sealed class GpuGraphicsOptions
{
    public GpuRenderRuntimeOptions Runtime { get; set; } = new();
}

/// <summary>A startup-only definition. GPU ownership belongs to its created runtime.</summary>
public interface IGpuRenderProviderDefinition
{
    ValueTask<IGpuRenderProvider> CreateAsync(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IGpuGraphicsSessionAccessor
{
    ValueTask<GpuGraphicsSession> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class GpuGraphicsSession
{
    internal GpuGraphicsSession(IGpuRenderRuntime runtime, GpuRenderContext? renderContext)
    {
        Runtime = runtime;
        RenderContext = renderContext;
    }
    public IGpuRenderRuntime Runtime { get; }
    public GpuRenderContext? RenderContext { get; }
}

public interface IGpuGraphicsPresentationFactory
{
    ValueTask<IGpuGraphicsPresentationConnection> CreateAsync(IGpuRenderRuntime runtime, CancellationToken cancellationToken);
}

public interface IGpuGraphicsPresentationConnection : IAsyncDisposable
{
    IGpuGraphPresentation Presentation { get; }
}

public sealed class LumyteGraphicsBuilder
{
    internal LumyteGraphicsBuilder(IServiceCollection services) => Services = services;
    public IServiceCollection Services { get; }

    public LumyteGraphicsBuilder Configure(Action<GpuGraphicsOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }

    public LumyteGraphicsBuilder BindConfiguration(IConfigurationSection section)
    {
        Services.AddOptions<GpuGraphicsOptions>().Bind(section);
        return this;
    }

    public LumyteGraphicsBuilder AddProvider(IGpuRenderProviderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Services.AddSingleton(definition);
        return this;
    }

    public LumyteGraphicsBuilder UsePresentation<TFactory>() where TFactory : class, IGpuGraphicsPresentationFactory
    {
        Services.AddScoped<IGpuGraphicsPresentationFactory, TFactory>();
        return this;
    }
}

public static class GraphicsHostServiceCollectionExtensions
{
    public static LumyteGraphicsBuilder AddLumyteGraphics(
        this IServiceCollection services,
        Action<GpuGraphicsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<GpuGraphicsOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        services.TryAddSingleton<GraphicsRuntimeHost>();
        services.TryAddSingleton<IGpuGraphicsSessionAccessor>(static provider => provider.GetRequiredService<GraphicsRuntimeHost>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GraphicsRuntimeHostedService>());
        return new(services);
    }
}

internal sealed class GraphicsRuntimeHostedService(GraphicsRuntimeHost owner) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => owner.StartAsync(cancellationToken);
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) { owner.Close(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { owner.Close(); return Task.CompletedTask; }
    public Task StoppedAsync(CancellationToken cancellationToken) => owner.StopAsync(cancellationToken);
}
