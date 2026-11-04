using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation;

internal sealed class UiPageRegistration<TArgs>(UiPageKey<TArgs> key, Func<TArgs, TArgs> capture, Func<IServiceProvider, IUiPage> create, Type? pageType) : UiPageRegistration(key.Value, typeof(TArgs), create, pageType)
{
    internal override UiPageDestination Capture<T>(UiPageKey<T> destination, T arguments)
    {
        if (typeof(T) != typeof(TArgs))
        {
            throw new ArgumentException("The route argument type must match its registration exactly.", nameof(destination));
        }

        return UiPageDestination.Capture(key, (TArgs)(object?)arguments!, capture);
    }
}
