namespace Lumyte.UI.Application.Diagnostics;

internal sealed class UiApplicationViewRegistry
{
    private readonly Dictionary<Guid, UiApplicationWindowContext> _windows = [];

    internal IEnumerable<UiApplicationWindowContext> Windows => _windows.Values;

    internal void Track(UiApplicationWindowContext context) => _windows[context.Id] = context;

    internal void Remove(Guid id) => _windows.Remove(id);
}
