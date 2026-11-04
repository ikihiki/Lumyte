using Lumyte.UI.Navigation;

namespace Lumyte.UI.Application.Navigation;

internal abstract class UiPageRegistration(string route, Type argumentsType, Func<IServiceProvider, IUiPage> create, Type? pageType)
{
    internal string Route { get; } = route;

    internal Type ArgumentsType { get; } = argumentsType;

    internal Type? PageType { get; } = pageType;

    internal IUiPage Create(IServiceProvider services) => create(services) ?? throw new InvalidOperationException("The page factory returned null.");

    internal abstract UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments);
}
