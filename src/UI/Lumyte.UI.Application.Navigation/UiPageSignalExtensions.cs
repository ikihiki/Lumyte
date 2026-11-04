using Lumyte.Signals;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Connects optional business observers and asynchronous operation lifetimes to a page activation.</summary>
public static class UiPageSignalExtensions
{
    /// <summary>Creates a page-owned observer and asynchronous operation group without owning shared signal values.</summary>
    /// <param name="context">The active preparation or construction context.</param>
    /// <returns>The group, released before page DI services.</returns>
    public static SignalScope CreateSignalScope(this UiPageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = new SignalScope();
        context.Own(scope);
        return scope;
    }
}
