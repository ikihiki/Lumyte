using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.Transport.Http;

/// <summary>Registers the browser-compatible HTTP implementation.</summary>
public static class HttpDiagnosticTransportServiceCollectionExtensions
{
    /// <summary>Chooses HTTP at the composition root.</summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">The endpoint and credential configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddHttpDiagnosticTransport(this IServiceCollection services, Action<HttpDiagnosticTransportOptions> configure)
    {
        var options = new HttpDiagnosticTransportOptions();
        configure(options);
        DiagnosticEndpoint.Validate(options.BaseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.GameToken);
        services.AddSingleton<IDiagnosticTransportFactory>(new HttpDiagnosticTransportFactory(options.BaseAddress, options.GameToken));
        return services;
    }
}
