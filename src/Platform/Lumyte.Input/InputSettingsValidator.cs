using Microsoft.Extensions.Options;

namespace Lumyte.Input;

internal sealed class InputSettingsValidator : IValidateOptions<InputSettings>
{
    public ValidateOptionsResult Validate(string? name, InputSettings options)
    {
        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        var errors = new List<string>();
        CheckZone(options.LeftStick, nameof(options.LeftStick), errors);
        CheckZone(options.RightStick, nameof(options.RightStick), errors);
        CheckZone(options.LeftTrigger, nameof(options.LeftTrigger), errors);
        CheckZone(options.RightTrigger, nameof(options.RightTrigger), errors);
        if (options.Bindings is null)
        {
            errors.Add("Bindings cannot be null.");
        }
        else
        {
            foreach ((string context, Dictionary<string, string[]> actions) in options.Bindings)
            {
                if (string.IsNullOrWhiteSpace(context) || actions is null)
                {
                    errors.Add("Each context needs a stable ID and an action dictionary.");
                    continue;
                }

                foreach ((string action, string[] bindings) in actions)
                {
                    if (string.IsNullOrWhiteSpace(action) || bindings is null)
                    {
                        errors.Add($"{context}: Each action needs a stable ID and a binding array.");
                        continue;
                    }

                    if (bindings.Any(binding => !IsBinding(binding)) || bindings.Distinct(StringComparer.Ordinal).Count() != bindings.Length)
                    {
                        errors.Add($"{context}/{action}: Invalid or duplicate physical bindings.");
                    }
                }
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    internal static bool IsBinding(string? binding)
    {
        if (binding is null)
        {
            return false;
        }

        string[] parts = binding.Split(':');
        return parts.Length == 2 && parts[0] switch
        {
            "key" => Enum.GetNames<Key>().Contains(parts[1], StringComparer.Ordinal) && parts[1] != nameof(Key.Unknown),
            "mouse" => Enum.GetNames<MouseButton>().Contains(parts[1], StringComparer.Ordinal),
            "controller" => Enum.GetNames<ControllerButton>().Contains(parts[1], StringComparer.Ordinal),
            _ => false,
        };
    }

    private static void CheckZone(DeadZoneSettings? zone, string path, List<string> errors)
    {
        if (zone is null || !float.IsFinite(zone.Inner) || !float.IsFinite(zone.Outer) || zone.Inner < 0 || zone.Inner >= zone.Outer || zone.Outer > 1)
        {
            errors.Add($"{path}: Expected finite 0 <= Inner < Outer <= 1.");
        }
    }
}
