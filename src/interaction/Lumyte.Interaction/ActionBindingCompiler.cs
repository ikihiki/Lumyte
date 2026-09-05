using System.Numerics;

namespace Lumyte.Interaction;

public static class ActionBindingCompiler
{
    public static IReadOnlyList<ActionMap> Compile(
        IEnumerable<ActionMap> defaults,
        ActionBindingDocument document)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(document);
        var maps = new List<ActionMap>();
        foreach (ActionMap map in defaults)
        {
            var bindings = new List<ActionBinding>();
            foreach (ActionBinding binding in map.Bindings)
            {
                string prefix = ActionBindingIdentity.GetPrefix(map.Name, binding);
                bindings.Add(CompileBinding(binding, prefix, document));
            }

            maps.Add(ActionMap.CreateEffective(
                map.Name,
                map.When,
                map.Priority,
                bindings));
        }

        return maps;
    }

    private static ActionBinding CompileBinding(
        ActionBinding binding,
        string prefix,
        ActionBindingDocument document) => binding switch
        {
            ActionBinding<bool> button => new ActionBinding<bool>(
                button.TypedAction,
                GetControl<bool>(document, prefix, ActionBindingPart.Primary),
                button.Processors.ToArray()) { BindingId = button.BindingId },
            ActionBinding<float> scalar => new ActionBinding<float>(
                scalar.TypedAction,
                GetControl<float>(document, prefix, ActionBindingPart.Primary),
                scalar.Processors.ToArray()) { BindingId = scalar.BindingId },
            ActionBinding<Vector2> vector => new ActionBinding<Vector2>(
                vector.TypedAction,
                GetControl<Vector2>(document, prefix, ActionBindingPart.Primary),
                vector.Processors.ToArray()) { BindingId = vector.BindingId },
            Vector2CompositeBinding composite => new Vector2CompositeBinding(
                composite.TypedAction,
                GetControl<bool>(document, prefix, ActionBindingPart.Up),
                GetControl<bool>(document, prefix, ActionBindingPart.Down),
                GetControl<bool>(document, prefix, ActionBindingPart.Left),
                GetControl<bool>(document, prefix, ActionBindingPart.Right),
                composite.Processors.ToArray()) { BindingId = composite.BindingId },
            _ => throw new NotSupportedException(
                $"Binding type {binding.GetType()} is not supported."),
        };

    private static InputControl<T> GetControl<T>(
        ActionBindingDocument document,
        string prefix,
        ActionBindingPart part)
    {
        InputControlDescriptor descriptor = document.GetSlot($"{prefix}:{part}").Control;
        return new(descriptor.Device, descriptor.Name);
    }
}
