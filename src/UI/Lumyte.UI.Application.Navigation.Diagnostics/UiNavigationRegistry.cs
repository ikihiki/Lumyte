using Lumyte.UI.Application.Diagnostics;

namespace Lumyte.UI.Application.Navigation.Diagnostics;

internal sealed class UiNavigationRegistry(UiApplicationViewRegistry views)
{
    internal IReadOnlyList<UiNavigationWindowSnapshot> Capture(Guid? id) => views.Windows.Where(context => (id is null || context.Id == id) && UiNavigationViewExtensions.IsConfigured(context.View)).Select(context => context.View.CaptureNavigationSnapshot()).ToArray();
}
