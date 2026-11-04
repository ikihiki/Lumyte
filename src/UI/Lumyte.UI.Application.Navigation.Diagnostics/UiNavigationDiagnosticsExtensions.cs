using Lumyte.Diagnostics;
using Lumyte.UI.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.Navigation.Diagnostics;

/// <summary>Registers optional navigation observation on the standard diagnostics point.</summary>
public static class UiNavigationDiagnosticsExtensions
{
    /// <summary>Adds navigation metadata after standard application diagnostics has been configured.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseNavigationDiagnostics(this UiApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(UiApplicationViewRegistry)))
        {
            throw new InvalidOperationException("Configure UseDiagnostics before UseNavigationDiagnostics.");
        }

        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(UiNavigationRegistry)))
        {
            throw new InvalidOperationException("Navigation diagnostics was already configured.");
        }

        builder.Services.AddSingleton<UiNavigationRegistry>();
        builder.Services.AddScoped(provider => new UiNavigationDiagnostics(provider.GetRequiredService<UiNavigationRegistry>()));
        builder.Services.AddDiagnosticSubsystem<UiNavigationDiagnostics, UiApplicationDiagnosticsPoint>(new("ui.navigation", "UI navigation", 1));
        return builder;
    }
}
