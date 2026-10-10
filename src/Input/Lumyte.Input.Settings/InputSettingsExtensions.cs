using Lumyte.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Input.Settings;

/// <summary>Registers persisted input settings with source-generated JSON metadata.</summary>
public static class InputSettingsExtensions
{
    /// <summary>Registers both input settings sections and their validators.</summary>
    /// <param name = "services">The services value.</param>
    /// <returns>The result of the operation.</returns>
    public static IServiceCollection AddInputSettings(this IServiceCollection services)
    {
        services.AddPersistedOptions<InputProcessingSettings>("input-processing").Validate(InputSettingsConverter.ValidateProcessing, "Invalid input processing settings.").UseJsonTypeInfo(InputSettingsJsonContext.Default.InputProcessingSettings);
        services.AddPersistedOptions<InputActionSettings>("input-actions").Validate(InputSettingsConverter.ValidateActions, "Invalid input action settings.").UseJsonTypeInfo(InputSettingsJsonContext.Default.InputActionSettings);
        return services;
    }
}
