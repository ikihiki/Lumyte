using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Graphics.ImageEffects.DependencyInjection;

/// <summary>Registers the optional scoped image effect renderer composition.</summary>
public static class ImageEffectsServiceCollectionExtensions
{
    /// <summary>Completes the four provided image effect renderer registrations.</summary>
    /// <remarks>
    /// Existing registrations are preserved, including when this method is called again.
    /// Later interface registrations take precedence for single-service resolution.
    /// No device, effect compiler, or service provider is created.
    /// </remarks>
    /// <param name="services">The collection to update before building a provider.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection UseImageEffects(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IImageEffectRenderer<ColorMatrixEffect>, DefaultColorMatrixEffectRenderer>();
        services.TryAddScoped<IImageEffectRenderer<GaussianBlurEffect>, DefaultGaussianBlurEffectRenderer>();
        services.TryAddScoped<IImageEffectRenderer<OpacityEffect>, DefaultOpacityEffectRenderer>();
        services.TryAddScoped<IImageEffectRenderer<CompositeEffect>, DefaultCompositeEffectRenderer>();
        return services;
    }
}
