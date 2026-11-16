using System.Numerics;

namespace Lumyte.UI.WebView;

/// <summary>Owner-only typed view requests; callbacks never receive an element or surface.</summary>
internal interface IWebViewHost
{
    /// <summary>Validates the owning surface before view publication.</summary>
    /// <param name="surface">The attached surface.</param>
    void ValidateSurface(UiSurface surface);

    /// <summary>Updates logical viewport and visibility.</summary>
    /// <param name="logicalSize">The finite layout size.</param>
    /// <param name="visible">Whether the view is visible.</param>
    void Viewport(Vector2 logicalSize, bool visible);

    /// <summary>Forwards input without synthesizing text from keys.</summary>
    /// <param name="input">The input transition.</param>
    /// <param name="localPosition">The optional body-local point.</param>
    void Input(UiInput input, Vector2? localPosition);

    /// <summary>Sets view focus.</summary>
    /// <param name="focused">The focus state.</param>
    void Focus(bool focused);

    /// <summary>Ends view input and visibility, retaining its session.</summary>
    void Detach();
}
