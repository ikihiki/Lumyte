using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lumyte.Diagnostics.Windowing;

/// <summary>Registers opt-in WindowSystem diagnostics at an existing execution point.</summary>
public static class WindowDiagnosticsExtensions
{
    /// <summary>Registers one scoped window registry and diagnostic subsystem.</summary>
    /// <typeparam name="TPoint">The owning game execution point.</typeparam>
    /// <param name="services">The game services.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddWindowDiagnostics<TPoint>(this IServiceCollection services)
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<WindowDiagnosticRegistry>();
        services.AddDiagnosticSubsystem<WindowDiagnostics, TPoint>(new("window", "Window", 1));
        return services;
    }
}
