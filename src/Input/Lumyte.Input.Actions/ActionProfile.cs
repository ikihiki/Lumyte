using System.Collections.Immutable;

namespace Lumyte.Input.Actions;
/// <summary>Contains immutable action, binding, context and recognition definitions.</summary>
/// <param name = "Actions">The Actions value.</param>
/// <param name = "Bindings">The Bindings value.</param>
/// <param name = "Contexts">The Contexts value.</param>
/// <param name = "Recognitions">The Recognitions value.</param>
public sealed record ActionProfile(ImmutableArray<ActionDefinition> Actions, ImmutableArray<ActionBinding> Bindings, ImmutableArray<InputContext> Contexts, ImmutableArray<RecognitionDefinition> Recognitions)
{
    /// <summary>Rejects invalid identifiers, references, tokens and parameter ranges.</summary>
    public void Validate()
    {
        if (Actions.IsDefault || Bindings.IsDefault || Contexts.IsDefault || Recognitions.IsDefault)
        {
            throw new ArgumentException("Profile arrays must be initialized.");
        }

        Unique(Actions.Select(action => action.Id));
        Unique(Bindings.Select(binding => binding.Id));
        Unique(Contexts.Select(context => context.Id));
        foreach (IGrouping<string, RecognitionDefinition> group in Recognitions.GroupBy(recognition => recognition.ContextId))
        {
            Unique(group.Select(recognition => recognition.Id));
        }

        var contexts = Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal);
        foreach (InputContext context in Contexts)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            InputContext current = context;
            while (true)
            {
                if (!visited.Add(current.Id))
                {
                    throw new ArgumentException("Context inheritance contains a cycle.");
                }

                if (current.ParentId is null)
                {
                    break;
                }

                if (!contexts.TryGetValue(current.ParentId, out current!))
                {
                    throw new ArgumentException("Unknown parent context.");
                }
            }

            Unique(LocalActions(context).Select(action => action.Id));
        }

        ActionDefinition[] definitions = Actions.Concat(Contexts.SelectMany(context => LocalActions(context))).ToArray();
        if (definitions.GroupBy(action => action.Id).Any(group => group.Select(action => action.Kind).Distinct().Count() > 1))
        {
            throw new ArgumentException("An action must have the same value kind in every context.");
        }

        foreach (ActionDefinition action in definitions)
        {
            if (!Enum.IsDefined(action.Kind) || !float.IsFinite(action.Sensitivity) || action.Sensitivity < 0 || !float.IsFinite(action.SmoothingSeconds) || action.SmoothingSeconds < 0)
            {
                throw new ArgumentException("Invalid action correction.");
            }
        }

        foreach (ActionBinding binding in Bindings)
        {
            if (!Available(binding.ContextId, binding.ActionId, contexts) || !float.IsFinite(binding.Scale.X) || !float.IsFinite(binding.Scale.Y) || !float.IsFinite(binding.ReleaseThreshold) || binding.ReleaseThreshold < 0 || binding.PressThreshold <= binding.ReleaseThreshold || !float.IsFinite(binding.PressThreshold))
            {
                throw new ArgumentException("Invalid binding.");
            }

            Type controlType = binding.Control.Kind switch
            {
                InputControlKind.Key => typeof(Key),
                InputControlKind.MouseButton => typeof(MouseButton),
                InputControlKind.ControllerButton => typeof(ControllerButton),
                InputControlKind.ControllerStick => typeof(ControllerStick),
                InputControlKind.ControllerTrigger => typeof(ControllerTrigger),
                _ => throw new ArgumentException("Invalid control kind."),
            };
            if (!Enum.IsDefined(controlType, binding.Control.Index))
            {
                throw new ArgumentException("Invalid control index.");
            }
        }

        foreach (RecognitionDefinition recognition in Recognitions)
        {
            if (!Enum.IsDefined(recognition.Kind) || !Contexts.Any(context => context.Id == recognition.ContextId) || recognition.Actions.IsDefaultOrEmpty || recognition.Actions.Any(id => !Available(recognition.ContextId, id, contexts)) || recognition.Window <= TimeSpan.Zero || recognition.TapCount < 1)
            {
                throw new ArgumentException("Invalid recognizer.");
            }
        }
    }

    internal static ImmutableArray<ActionDefinition> LocalActions(InputContext context)
        => context.Actions.IsDefault ? [] : context.Actions;

    private bool Available(string contextId, string actionId, Dictionary<string, InputContext> contexts)
    {
        if (!contexts.TryGetValue(contextId, out InputContext? current))
        {
            return false;
        }

        while (true)
        {
            if (LocalActions(current).Any(action => action.Id == actionId))
            {
                return true;
            }

            if (current.ParentId is null)
            {
                return Actions.Any(action => action.Id == actionId);
            }

            current = contexts[current.ParentId];
        }
    }

    private static void Unique(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                throw new ArgumentException("Identifiers must be nonempty and unique.");
            }
        }
    }
}
