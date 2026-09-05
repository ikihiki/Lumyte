using System.Numerics;

namespace Lumyte.Interaction;

internal static class ActionBindingIdentity
{
    public static string GetPrefix(string mapName, ActionBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(binding);
        string bindingId = binding.BindingId ?? GetStructuralId(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        return $"{mapName}/{bindingId}";
    }

    private static string GetStructuralId(ActionBinding binding) => binding switch
    {
        ActionBinding<bool> button =>
            $"{binding.Action.Id}/button/{button.TypedControl}",
        ActionBinding<float> scalar =>
            $"{binding.Action.Id}/scalar/{scalar.TypedControl}",
        ActionBinding<Vector2> vector =>
            $"{binding.Action.Id}/vector2/{vector.TypedControl}",
        Vector2CompositeBinding composite =>
            $"{binding.Action.Id}/vector2-composite/"
            + $"{composite.Up}/{composite.Down}/{composite.Left}/{composite.Right}",
        _ => throw new NotSupportedException(
            $"Binding type {binding.GetType()} is not supported."),
    };
}
