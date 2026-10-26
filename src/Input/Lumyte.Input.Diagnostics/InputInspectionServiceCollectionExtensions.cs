using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Settings;
using Lumyte.Input.Actions;
using Lumyte.Input.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Input.Diagnostics;

/// <summary>Registers one shared Input inspection session per game scope.</summary>
public static class InputInspectionServiceCollectionExtensions
{
    /// <summary>Registers Input and Action inspection aliases and the diagnostic contributor.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddInputInspection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<InputInspectionSession>();
        services.AddScoped<IInputInspection>(provider => provider.GetRequiredService<InputInspectionSession>());
        services.AddScoped<IActionInspection>(provider => provider.GetRequiredService<InputInspectionSession>());
        services.AddScoped<IInputOverrideService>(provider => provider.GetRequiredService<InputInspectionSession>());
        services.AddScoped<IInputTapeService>(provider => provider.GetRequiredService<InputInspectionSession>());
        services.AddScoped<IInputLiveService>(provider => provider.GetRequiredService<InputInspectionSession>());
        services.AddScoped<InputDiagnostics>();
        return services;
    }

    /// <summary>Registers inspection and publishes it at an existing diagnostic execution point.</summary>
    /// <typeparam name="TPoint">The owning-thread execution point.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="descriptor">The optional Input subsystem descriptor.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddInputDiagnostics<TPoint>(this IServiceCollection services, SubsystemDescriptor? descriptor = null)
        where TPoint : class
    {
        services.AddInputInspection();
        services.AddDiagnosticSubsystem<InputDiagnostics, TPoint>(descriptor ?? new("input", "Input", 1));
        return services;
    }

    /// <summary>Publishes the persisted Action mapping tree through the shared settings diagnostics.</summary>
    /// <typeparam name="TPoint">The owning-thread execution point.</typeparam>
    /// <param name="services">The service collection containing <c>AddInputSettings()</c>.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddInputActionSettingsDiagnostics<TPoint>(this IServiceCollection services)
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSettingsDiagnostics<InputActionSettings, TPoint>("input-actions", _ => { });
        return services;
    }
}
