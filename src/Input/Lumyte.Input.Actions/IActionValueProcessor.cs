namespace Lumyte.Input.Actions;

/// <summary>Corrects an action value after device mapping.</summary>
public interface IActionValueProcessor
{
    /// <summary>Transforms a mapped action value.</summary>
    /// <param name = "mapped">The mapped state.</param>
    /// <param name = "now">Monotonic input time.</param>
    /// <returns>The corrected state.</returns>
    ActionState Process(ActionState mapped, TimeSpan now);

    /// <summary>Discards transient correction state.</summary>
    void Reset();
}
