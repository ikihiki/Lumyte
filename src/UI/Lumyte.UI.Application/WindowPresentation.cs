namespace Lumyte.UI.Application;

/// <summary>Chooses a normal window's presentation independently of popup capabilities.</summary>
public enum WindowPresentation
{
    /// <summary>Uses native when supported, otherwise a hosted window.</summary>
    Automatic,

    /// <summary>Uses an owner presentation region.</summary>
    Hosted,

    /// <summary>Requires a separate native window.</summary>
    NativeRequired,
}
