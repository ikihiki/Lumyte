using Lumyte.UI.WebView;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Registers the explicit optional process-isolated browser backend.</summary>
public static class CefWebViewApplicationExtensions
{
    /// <summary>Registers one CEF runtime; the optional helper starts asynchronously on explicit session use.</summary>
    /// <param name="builder">The mutable builder.</param>
    /// <param name="options">The explicit runtime budgets.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseCefWebViews(this UiApplicationBuilder builder, CefWebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.CheckMutable();
        options = options with { AllowedMessageOrigins = CefMessageOrigins.Snapshot(options.AllowedMessageOrigins) };
        if (options.MaximumSessions != 1 || options.StartupTimeout <= TimeSpan.Zero || options.StartupTimeout > TimeSpan.FromMinutes(2) || options.NavigationTimeout <= TimeSpan.Zero || options.NavigationTimeout > TimeSpan.FromMinutes(2) || options.CpuPixelBudgetBytes <= 0 || options.GpuImageBudgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (builder.Services.Any(service => service.ServiceType == typeof(CefWebViewParticipant)))
        {
            throw new InvalidOperationException("cef-already-registered");
        }

        builder.BuildValidators.Add(_ =>
        {
            if (builder.IntegrationDriverKind != "desktop")
            {
                throw new NotSupportedException("cef-desktop-required");
            }
        });
        builder.Services.AddSingleton(provider => new CefWebViewParticipant(options, provider.GetRequiredService<IUiDispatcher>()));
        builder.Participants.Add(provider => provider.GetRequiredService<CefWebViewParticipant>());
        return builder;
    }

    /// <summary>Gets the same session on declaration rebuild; never renavigates an existing session.</summary>
    /// <param name="window">The owning window instance.</param>
    /// <param name="key">A nonempty stable key.</param>
    /// <param name="initialUri">The immutable initial HTTP(S) address.</param>
    /// <returns>The borrowed session.</returns>
    public static UiWebViewSession GetWebViewSession(this UiApplicationWindowContext window, string key, Uri initialUri)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.CheckOwner();
        return window.Services.GetRequiredService<CefWebViewParticipant>().GetSession(window, key, initialUri);
    }
}
