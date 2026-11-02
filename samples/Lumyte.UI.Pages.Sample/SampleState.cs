using Lumyte.Signals;
using Lumyte.UI.Application;
using Lumyte.UI.Navigation;

namespace Lumyte.UI.Pages.SampleSupport;

internal sealed class SampleState
{
    internal UiApplicationWindowContext? Context { get; set; }

    internal IUiNavigator? Navigation { get; set; }

    internal Action Stop { get; set; } = static () => throw new InvalidOperationException("The sample has not started.");

    internal Signal<float?> Width { get; } = Signal.Create<float?>(160);

    internal int Created { get; set; }

    internal int Disposed { get; set; }
}
