using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>Registers opt-in scoped automation at an existing diagnostic execution point.</summary>
public static class UiAutomationDiagnosticExtensions
{
    /// <summary>Adds the runtime and generated subsystem without adding a transport or opening a socket.</summary>
    /// <typeparam name="TPoint">The existing owner execution point.</typeparam>
    /// <param name="services">Application services.</param>
    /// <param name="options">Optional bounded capacities.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddUiAutomationDiagnostics<TPoint>(this IServiceCollection services, UiAutomationDiagnosticsOptions? options = null)
        where TPoint : class
    {
        ArgumentNullException.ThrowIfNull(services);
        UiAutomationDiagnosticsOptions configured = options ?? new();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped(provider => new UiAutomationDiagnosticsRuntime(provider.GetRequiredService<TimeProvider>(), configured)
        {
            Enabled = provider.GetRequiredService<DiagnosticOptions>().Enabled,
            Logger = provider.GetService<ILogger<UiAutomationDiagnosticsRuntime>>(),
            InstanceId = provider.GetRequiredService<IGameExecutionIdentity>().InstanceId,
        });
        services.AddDiagnosticSubsystem<UiAutomationDiagnostics, TPoint>(new("ui.automation", "UI automation", 1));
        return services;
    }
}
