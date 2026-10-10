namespace Lumyte.Input.Actions;

/// <summary>Recognizes operations from context-scoped action events.</summary>
public interface IActionRecognizer
{
    /// <summary>Consumes new action events and advances timers, including empty batches.</summary>
    /// <param name = "events">Ordered context events.</param>
    /// <param name = "now">Monotonic input time.</param>
    /// <returns>The completed operations.</returns>
    IReadOnlyList<RecognizedAction> Advance(IReadOnlyList<ActionEvent> events, TimeSpan now);

    /// <summary>Discards incomplete operations.</summary>
    void Reset();
}
