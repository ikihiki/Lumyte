namespace Lumyte.Diagnostics.Animation;

/// <summary>Registers opt-in animation diagnostics at an existing execution point.</summary>
public static class AnimationDiagnosticsExtensions
{
    /// <summary>Registers one scoped animation registry and diagnostic subsystem.</summary>
    /// <typeparam name="TPoint">The owning game execution point.</typeparam>
    /// <param name="services">The game services.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddAnimationDiagnostics<TPoint>(this IServiceCollection services)
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AnimationDiagnosticRegistry>();
        services.AddDiagnosticSubsystem<AnimationDiagnostics, TPoint>(new("animation", "Animation", 1));
        return services;
    }
}
