using Lumyte.Diagnostics;
using Lumyte.Diagnostics.UI.Automation;

namespace Lumyte.UI.Application.Diagnostics;

/// <summary>Registers optional diagnostics without transport side effects.</summary>
public static class UiApplicationDiagnosticsExtensions
{
    /// <summary>Adds one scoped diagnostics runtime for all application windows.</summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="options">Target permissions and runtime limits.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseDiagnostics(this UiApplicationBuilder builder, UiApplicationDiagnosticsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        UiApplicationDiagnosticsOptions configured = options ?? new();
        if (string.IsNullOrEmpty(configured.TargetName) || configured.TargetName.Length > 80 || configured.TargetName.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Invalid diagnostic target name.", nameof(options));
        }

        if (builder.Participants.Any(factory => factory.Target is Registration))
        {
            throw new InvalidOperationException("Diagnostics was already configured.");
        }

        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(DiagnosticOptions)))
        {
            builder.Services.AddLumyteDiagnostics(value => value.Enabled = true);
        }

        builder.Services.AddDiagnosticExecutionPoint<UiApplicationDiagnosticsPoint>("ui.application");
        builder.Services.AddUiAutomationDiagnostics<UiApplicationDiagnosticsPoint>(configured.Runtime);
        var registration = new Registration(configured);
        builder.Participants.Add(registration.Create);
        return builder;
    }

    private sealed class Registration(UiApplicationDiagnosticsOptions options)
    {
        internal IUiApplicationParticipant Create(IServiceProvider services) => new UiApplicationDiagnosticsParticipant(services, options);
    }
}
