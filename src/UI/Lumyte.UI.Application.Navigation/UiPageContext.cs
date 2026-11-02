using Lumyte.UI.Navigation;
using Lumyte.UI.TextEditing;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Provides immutable borrowed services for one page activation.</summary>
public sealed partial class UiPageContext
{
    internal UiPageContext(UiPageActivationContext activation, IUiTextLayoutProvider? textLayoutProvider)
    {
        Activation = activation;
        TextLayoutProvider = textLayoutProvider;
    }

    /// <summary>Gets the activation, destination, navigator, dispatcher and cancellation lifetime.</summary>
    public UiPageActivationContext Activation { get; }

    /// <summary>Gets the window's borrowed text layout provider, if configured.</summary>
    public IUiTextLayoutProvider? TextLayoutProvider { get; }
}
