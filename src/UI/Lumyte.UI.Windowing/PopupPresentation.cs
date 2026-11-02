namespace Lumyte.UI.Windowing;

/// <summary>Selects the popup's presentation capability policy.</summary>
public enum PopupPresentation
{
    /// <summary>Prefers native and falls back to hosted.</summary>
    Automatic,

    /// <summary>Always uses a hosted overlay.</summary>
    Hosted,

    /// <summary>Requires a native window and reports failure when unavailable.</summary>
    NativeRequired,
}
