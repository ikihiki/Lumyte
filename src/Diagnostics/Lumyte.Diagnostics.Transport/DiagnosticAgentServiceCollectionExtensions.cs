using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.Transport;

/// <summary>Registers the protocol-independent game agent.</summary>
public static class DiagnosticAgentServiceCollectionExtensions
{
    /// <summary>Registers one agent per game scope and execution point.</summary>
    /// <typeparam name="TPoint">The execution point.</typeparam>
    /// <param name="services">The services.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddDiagnosticAgent<TPoint>(this IServiceCollection services)
        where TPoint : class
    {
        services.AddScoped<DiagnosticAgent<TPoint>>();
        return services;
    }
}
