using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Registers the desktop StreamingHub implementation.</summary>
public static class MagicOnionDiagnosticTransportServiceCollectionExtensions
{
    /// <summary>Chooses MagicOnion at the composition root.</summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">The endpoint and credential configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddMagicOnionDiagnosticTransport(this IServiceCollection services, Action<MagicOnionDiagnosticTransportOptions> configure)
    {
        var options = new MagicOnionDiagnosticTransportOptions();
        configure(options);
        DiagnosticEndpoint.Validate(options.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GameToken);
        services.AddSingleton<IDiagnosticTransportFactory>(new MagicOnionDiagnosticTransportFactory(options.Endpoint, options.GameToken));
        return services;
    }
}
