using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Connects graph inspection to an existing game execution point.</summary>
public static class RenderGraphDiagnosticsExtensions
{
    /// <summary>Registers a scoped observer and Observe-only diagnostic contributor.</summary>
    /// <typeparam name="TPoint">The owning game execution point.</typeparam>
    /// <param name="services">The services with Lumyte diagnostics and the execution point registered.</param>
    /// <param name="moduleId">The unique subsystem ID.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddRenderGraphDiagnostics<TPoint>(this IServiceCollection services, string moduleId = "graphics.render-graph")
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDiagnosticSubsystem<RenderGraphDiagnostics, TPoint>(new(moduleId, "Render graph", 1));
        return services;
    }
}
