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
        Unique(Recognitions.Select(recognition => recognition.Id));
        foreach (ActionDefinition action in Actions)
        {
            if (!Enum.IsDefined(action.Kind) || !float.IsFinite(action.Sensitivity) || action.Sensitivity < 0 || !float.IsFinite(action.SmoothingSeconds) || action.SmoothingSeconds < 0)
            {
                throw new ArgumentException("Invalid action correction.");
            }
        }

        foreach (ActionBinding binding in Bindings)
        {
            if (!Actions.Any(action => action.Id == binding.ActionId) || !Contexts.Any(context => context.Id == binding.ContextId) || !float.IsFinite(binding.Scale.X) || !float.IsFinite(binding.Scale.Y) || !float.IsFinite(binding.ReleaseThreshold) || binding.ReleaseThreshold < 0 || binding.PressThreshold <= binding.ReleaseThreshold || !float.IsFinite(binding.PressThreshold))
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
            if (!Enum.IsDefined(recognition.Kind) || !Contexts.Any(context => context.Id == recognition.ContextId) || recognition.Actions.IsDefaultOrEmpty || recognition.Actions.Any(id => !Actions.Any(action => action.Id == id)) || recognition.Window <= TimeSpan.Zero || recognition.TapCount < 1)
            {
                throw new ArgumentException("Invalid recognizer.");
            }
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
