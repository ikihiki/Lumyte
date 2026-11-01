using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Holds test-owned runtime handles without adding instance lookup to product declarations.</summary>
public abstract class UiElementTestBase
{
    private readonly List<UiSurface> _surfaces = [];
    private readonly Dictionary<Widget, UiElement> _instances = new(ReferenceEqualityComparer.Instance);

    /// <summary>Mounts a declaration and remembers runtime handles for input assertions.</summary>
    /// <param name="root">The test declaration.</param>
    /// <returns>The surface owned and disposed by the test.</returns>
    protected UiSurface Mount(Widget root)
    {
        var surface = new UiSurface(root);
        _surfaces.Add(surface);
        foreach (UiElement element in UiSurface.CollectElements(surface.RootElement))
        {
            _instances[element.Widget] = element;
        }

        return surface;
    }

    /// <summary>Reads button state from its runtime instance, including a recently removed instance.</summary>
    /// <param name="declaration">The current or previously adopted test configuration.</param>
    /// <returns>The runtime press state.</returns>
    protected bool Pressed(Widget declaration)
    {
        if (_instances.TryGetValue(declaration, out UiElement? known))
        {
            return ((ButtonElement)known).IsPressed;
        }

        foreach (UiSurface surface in _surfaces)
        {
            try
            {
                foreach (UiElement element in UiSurface.CollectElements(surface.RootElement))
                {
                    if (ReferenceEquals(element.Widget, declaration))
                    {
                        _instances.Add(declaration, element);
                        return ((ButtonElement)element).IsPressed;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        throw new InvalidOperationException("The test configuration was never mounted.");
    }
}
