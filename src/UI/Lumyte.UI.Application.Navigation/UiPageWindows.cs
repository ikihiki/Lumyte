using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Creates passive typed page factories for standard window declarations.</summary>
public static class UiPageWindows
{
    /// <summary>Specifies an initial page without creating a page or scope.</summary>
    /// <param name="page">The registered no-argument page.</param>
    /// <param name="options">The copied history limits.</param>
    /// <param name="onCompleted">The navigation result callback.</param>
    /// <returns>A factory that transfers a fresh controller to each window instance.</returns>
    public static Func<IServiceProvider, IUiApplicationContent> Page(UiPageKey<UiNoArgs> page, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null) => Page(page, default(UiNoArgs), options, onCompleted);

    /// <summary>Specifies a typed initial page, capturing arguments when the window instance starts.</summary>
    /// <typeparam name="TArgs">The exact registered argument type.</typeparam>
    /// <param name="page">The registered page.</param>
    /// <param name="arguments">Arguments copied by the registered copier at startup.</param>
    /// <param name="options">The copied history limits.</param>
    /// <param name="onCompleted">The navigation result callback.</param>
    /// <returns>A passive window content factory.</returns>
    public static Func<IServiceProvider, IUiApplicationContent> Page<TArgs>(UiPageKey<TArgs> page, TArgs arguments, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        UiNavigationOptions? copy = options is null ? null : options with { };
        if (copy is { MaxHistoryEntries: < 1 })
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        return services => new UiRegisteredNavigationContent<TArgs>(services.GetRequiredService<UiPageRegistry>(), page, arguments, copy, onCompleted);
    }
}
