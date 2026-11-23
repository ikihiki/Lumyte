namespace Lumyte.UI.Windowing;

/// <summary>Selects a dialog presentation without silently weakening NativeRequired.</summary>
public enum DialogPresentation
{
    /// <summary>Uses Hosted when native modal ownership is unavailable.</summary>
    Automatic,

    /// <summary>Requires in-process Hosted presentation.</summary>
    Hosted,

    /// <summary>Fails before content construction unless native modal ownership is supported.</summary>
    NativeRequired,
}
