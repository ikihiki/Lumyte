using Lumyte.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.Settings;

/// <summary>Opt-in settings diagnostics sharing the existing settings service.</summary>
public static class SettingsDiagnosticsExtensions
{
    /// <summary>Registers a scoped settings adapter at an existing execution point.</summary>
    /// <typeparam name="T">The persisted model.</typeparam>
    /// <typeparam name="TPoint">The owning game execution point.</typeparam>
    /// <param name="services">The game services.</param>
    /// <param name="moduleId">The stable lowercase module ID.</param>
    /// <param name="configure">The explicitly exposed fields.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddSettingsDiagnostics<T, TPoint>(this IServiceCollection services, string moduleId, Action<SettingsDiagnosticFields<T>> configure)
        where T : class, new()
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        if (moduleId.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            || services.Any(service => service.ServiceType == typeof(SettingsDiagnostics<T>)))
        {
            throw new ArgumentException("Settings diagnostic module IDs and model types must be unique.", nameof(moduleId));
        }

        var definition = new SettingsDiagnosticFields<T>();
        configure(definition);
        SettingsDiagnosticFields<T>.Field[] fields = definition.Seal();
        services.AddDiagnosticSubsystem<SettingsDiagnostics<T>, TPoint>(new("settings." + moduleId, "Settings: " + moduleId, 1));
        ServiceDescriptor adapter = services.Single(service => service.ServiceType == typeof(SettingsDiagnostics<T>));
        services.Remove(adapter);
        services.AddScoped(provider =>
        {
            IGameExecutionIdentity identity = provider.GetRequiredService<IGameExecutionIdentity>();
            using IDisposable correlation = SettingsTelemetry.BeginScope(identity.InstanceId);
            return new SettingsDiagnostics<T>(provider.GetRequiredService<IEditableOptions<T>>(), fields, identity);
        });
        return services;
    }
}
