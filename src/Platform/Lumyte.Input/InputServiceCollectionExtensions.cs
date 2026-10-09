using Lumyte.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lumyte.Input;

/// <summary>Enables Input and automatically registers its persistent settings.</summary>
public static class InputServiceCollectionExtensions
{
    /// <summary>Registers Input once; configure the shared source separately with AddSettings.</summary>
    /// <param name="services">The services to configure.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection UseInput(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(InputRegistration)))
        {
            return services;
        }

        services.AddSingleton(new InputRegistration());
        services.AddPersistedOptions<InputSettings>("input")
            .UseJsonDefinition<InputSettings, InputSettingsDefinition>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InputSettings>, InputSettingsValidator>());
        services.TryAddSingleton<InputSystem>();
        services.TryAddSingleton<InputSettingsProcessor>();
        return services;
    }

    private sealed class InputRegistration;
}
