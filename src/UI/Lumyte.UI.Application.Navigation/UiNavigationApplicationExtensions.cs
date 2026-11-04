using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Adds one navigation content controller without requiring DI page registration.</summary>
public static class UiNavigationApplicationExtensions
{
    /// <summary>Starts a typed initial page using the standard primary window.</summary>
    /// <typeparam name="TArgs">The registered argument type.</typeparam>
    /// <param name="builder">The passive application builder.</param>
    /// <param name="provider">The borrowed activation provider.</param>
    /// <param name="key">The initial static route.</param>
    /// <param name="arguments">The initial arguments, captured when navigation is requested.</param>
    /// <param name="options">The history limit.</param>
    /// <param name="onCompleted">The settled request callback.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseNavigation<TArgs>(this UiApplicationBuilder builder, IUiPageActivationProvider provider, UiPageKey<TArgs> key, TArgs arguments, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(key);
        return builder.UseContent(_ => new UiNavigationApplicationContent(provider, navigation => navigation.RequestNavigate(key, arguments), options, onCompleted));
    }
}
