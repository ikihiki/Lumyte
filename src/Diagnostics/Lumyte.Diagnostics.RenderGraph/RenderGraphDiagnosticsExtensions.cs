namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Connects graph inspection to an existing game execution point.</summary>
public static class RenderGraphDiagnosticsExtensions
{
    /// <summary>Registers scoped aggregate monitoring and bounded Observe-only captures.</summary>
    /// <typeparam name="TPoint">The owning game execution point.</typeparam>
    /// <param name="services">The services with Lumyte diagnostics and the execution point registered.</param>
    /// <param name="moduleId">The unique subsystem ID.</param>
    /// <param name="configure">Optional capture budgets and publication interval.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddRenderGraphDiagnostics<TPoint>(this IServiceCollection services, string moduleId = "graphics.render-graph", Action<RenderGraphDiagnosticOptions>? configure = null)
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RenderGraphDiagnosticOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddDiagnosticSubsystem<RenderGraphDiagnostics, TPoint>(new(moduleId, "Render graph", 2));
        services.AddSingleton(options);
        return services;
    }
}
