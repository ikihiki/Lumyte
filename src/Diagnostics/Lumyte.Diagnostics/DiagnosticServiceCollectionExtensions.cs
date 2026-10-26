using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics;

/// <summary>Composition-root registration of generated diagnostic adapters.</summary>
public static class DiagnosticServiceCollectionExtensions
{
    /// <summary>Registers bounded collection of standard telemetry.</summary>
    /// <param name="services">The services argument.</param>
    /// <param name="configure">The configure argument.</param>
    /// <returns>The computed result.</returns>
    public static IServiceCollection AddLumyteDiagnostics(this IServiceCollection services, Action<DiagnosticOptions> configure)
    {
        var options = new DiagnosticOptions();
        configure(options);
        if (options.QueueCapacity < 1 || !double.IsFinite(options.TraceSampleRatio) || options.TraceSampleRatio is < 0 or > 1)
        {
            throw new ArgumentException("Invalid collection limits.", nameof(configure));
        }

        services.AddMetrics();
        services.AddLogging();
        services.AddSingleton(options);
        services.TryAddSingleton<TelemetryRouter>();
        services.TryAddScoped<IGameExecutionIdentity, GameExecutionIdentity>();
        services.AddScoped(provider => new DiagnosticTelemetry(provider.GetRequiredService<IGameExecutionIdentity>(), options, provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), provider.GetRequiredService<TelemetryRouter>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DiagnosticLoggerProvider>());
        return services;
    }

    /// <summary>Registers a scoped queue.</summary>
    /// <typeparam name="TPoint">The marker.</typeparam>
    /// <param name="services">The services argument.</param>
    /// <param name="id">The id argument.</param>
    /// <returns>The computed result.</returns>
    public static IServiceCollection AddDiagnosticExecutionPoint<TPoint>(this IServiceCollection services, string id)
        where TPoint : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (services.Any(item => item.ServiceType == typeof(IDiagnosticPump<TPoint>)
            || (item.ImplementationInstance is DiagnosticExecutionPointRegistration point && point.Id == id)))
        {
            throw new ArgumentException("Duplicate execution point.", nameof(services));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new DiagnosticExecutionPointRegistration(typeof(TPoint), id));
        services.AddScoped<IDiagnosticPump<TPoint>, DiagnosticPump<TPoint>>();
        return services;
    }

    /// <summary>Registers one adapter, sharing its scoped instance.</summary>
    /// <typeparam name="TContributor">The generated adapter.</typeparam>
    /// <typeparam name="TPoint">The marker.</typeparam>
    /// <param name="services">The services argument.</param>
    /// <param name="descriptor">The descriptor argument.</param>
    /// <returns>The computed result.</returns>
    public static IServiceCollection AddDiagnosticSubsystem<TContributor, TPoint>(this IServiceCollection services, SubsystemDescriptor descriptor)
        where TContributor : class, IDiagnosticContributor
        where TPoint : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
        if (descriptor.SchemaVersion < 1 || !services.Any(item => item.ServiceType == typeof(IDiagnosticPump<TPoint>)))
        {
            throw new ArgumentException("Register a valid execution point first.", nameof(descriptor));
        }

        ServiceDescriptor[] existing = services.Where(item => item.ServiceType == typeof(TContributor)).ToArray();
        if (existing.Length > 1 || existing.Any(item => item.Lifetime != ServiceLifetime.Scoped)
            || services.Any(item => item.ImplementationInstance is DiagnosticRegistration registration
                && (registration.Descriptor.Id == descriptor.Id || registration.Contributor == typeof(TContributor))))
        {
            throw new ArgumentException("Duplicate subsystem or invalid contributor lifetime.", nameof(services));
        }

        services.TryAddScoped<TContributor>();
        services.AddSingleton(new DiagnosticRegistration(typeof(TPoint), typeof(TContributor), descriptor, static provider => provider.GetRequiredService<TContributor>()));
        return services;
    }
}
