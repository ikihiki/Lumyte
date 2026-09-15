using Lumyte.Graphics.RenderGraph;

using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Graphics.Hosting;

/// <summary>Transfers a surface adapter to Generic Host. The application keeps its window alive until Host shutdown completes.</summary>
public sealed class GpuGraphicsSurfaceConnection(GpuSurfacePresentation presentation) : IGpuGraphicsPresentationConnection
{
    public IGpuGraphPresentation Presentation { get; } = presentation ?? throw new ArgumentNullException(nameof(presentation));
    public ValueTask DisposeAsync() => presentation.DisposeAsync();
}

public static class GraphicsPresentationHostingExtensions
{
    /// <summary>Registers runtime-specific surface preparation inside the selected runtime's DI scope.</summary>
    public static LumyteGraphicsBuilder UsePresentation(this LumyteGraphicsBuilder builder,
        Func<IServiceProvider, IGpuRenderRuntime, CancellationToken, ValueTask<IGpuGraphicsPresentationConnection>> create)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(create);
        builder.Services.AddScoped<IGpuGraphicsPresentationFactory>(services => new Factory(services, create));
        return builder;
    }
    private sealed class Factory(IServiceProvider services, Func<IServiceProvider, IGpuRenderRuntime, CancellationToken, ValueTask<IGpuGraphicsPresentationConnection>> create) : IGpuGraphicsPresentationFactory
    { public ValueTask<IGpuGraphicsPresentationConnection> CreateAsync(IGpuRenderRuntime runtime, CancellationToken cancellationToken) => create(services, runtime, cancellationToken); }
}
