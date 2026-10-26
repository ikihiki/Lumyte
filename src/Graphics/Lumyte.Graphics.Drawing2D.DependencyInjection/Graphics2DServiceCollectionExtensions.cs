using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Graphics.Drawing2D.DependencyInjection;

/// <summary>Registers the optional scoped command renderer composition.</summary>
public static class Graphics2DServiceCollectionExtensions
{
    /// <summary>Completes the standard drawing command renderer registrations.</summary>
    /// <remarks>
    /// Existing registrations are preserved, including when this method is called again.
    /// Register a replacement interface after this call to select it through single-service
    /// resolution. No device, renderer, effect compiler, or service provider is created.
    /// </remarks>
    /// <param name="services">The collection to update before building a provider.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection UseGraphics2D(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IFillRectRenderer2D, DefaultFillRectRenderer2D>();
        services.TryAddScoped<IFillRoundedRectRenderer2D, DefaultFillRoundedRectRenderer2D>();
        services.TryAddScoped<IFillCircleRenderer2D, DefaultFillCircleRenderer2D>();
        services.TryAddScoped<DrawingCommandRenderers2D>();
        return services;
    }
}
