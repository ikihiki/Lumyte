using Lumyte.UI.WebView;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Registers the explicit experimental sandboxed browser integration.</summary>
public static class CefWebViewApplicationExtensions
{
    /// <summary>Registers one CEF runtime; native startup occurs only when the application starts.</summary>
    /// <param name="builder">The mutable builder.</param>
    /// <param name="options">The explicit runtime budgets.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseCefWebViews(this UiApplicationBuilder builder, CefWebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.CheckMutable();
        if (options.MaximumSessions <= 0 || options.CpuPixelBudgetBytes <= 0 || options.GpuImageBudgetBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.RequireIme)
        {
            throw new NotSupportedException("cef-ime-unavailable");
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
