using System.Numerics;

namespace Lumyte.Input.Actions;

/// <summary>Observes action definitions and evaluated state for one action system.</summary>
public interface IActionInspection
{
    /// <summary>Registers an action system and its initial profile.</summary>
    /// <param name="system">The action system.</param>
    /// <param name="profile">The initial profile.</param>
    void SystemCreated(ActionSystem system, ActionProfile profile);

    /// <summary>Replaces the definitions associated with a registered system.</summary>
    /// <param name="system">The action system.</param>
    /// <param name="profile">The replacement profile.</param>
    void ProfileChanged(ActionSystem system, ActionProfile profile);

    /// <summary>Applies an optional diagnostic value after action correction and before phase recognition.</summary>
    /// <param name="system">The action system.</param>
    /// <param name="contextId">The effective context identifier.</param>
    /// <param name="actionId">The action identifier.</param>
    /// <param name="kind">The declared action kind.</param>
    /// <param name="value">The corrected mapped value.</param>
    /// <param name="now">The current logical input time.</param>
    /// <returns>The effective value.</returns>
    Vector2 EvaluateValue(ActionSystem system, string contextId, string actionId, ActionValueKind kind, Vector2 value, TimeSpan now) => value;

    /// <summary>Observes one completed evaluation without consuming its action buffer.</summary>
    /// <param name="system">The action system.</param>
    /// <param name="events">The state changes from this evaluation.</param>
    /// <param name="recognized">The recognitions from this evaluation.</param>
    /// <param name="now">The completed logical time.</param>
    void Advanced(ActionSystem system, ReadOnlyMemory<ActionEvent> events, ReadOnlyMemory<RecognizedAction> recognized, TimeSpan now);

    /// <summary>Removes state belonging to a disposed system.</summary>
    /// <param name="system">The action system.</param>
    void SystemDisposed(ActionSystem system);
}
