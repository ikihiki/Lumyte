namespace Lumyte.Interaction;

public sealed class InputAction<T>(
    string id,
    ActionValueAggregation aggregation = ActionValueAggregation.MaximumMagnitude)
    : InteractionIntent(id)
{
    public ActionValueAggregation Aggregation { get; } = aggregation;
}
