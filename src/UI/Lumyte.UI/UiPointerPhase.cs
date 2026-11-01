namespace Lumyte.UI;

/// <summary>The supported primary pointer transitions.</summary>
public enum UiPointerPhase
{
    /// <summary>Updates the captured pointer's position.</summary>
    Move,

    /// <summary>Begins a primary press.</summary>
    Down,

    /// <summary>Ends a primary press.</summary>
    Up,

    /// <summary>Abandons a primary press.</summary>
    Cancel,
}
