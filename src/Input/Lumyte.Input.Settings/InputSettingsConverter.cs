using System.Collections.Immutable;
using System.Numerics;
using Lumyte.Input.Actions;
using Lumyte.Input.Processing;

namespace Lumyte.Input.Settings;

/// <summary>Converts stable settings tokens to validated runtime definitions.</summary>
public static class InputSettingsConverter
{
    /// <summary>Creates and validates an immutable runtime action profile.</summary>
    /// <param name = "settings">The settings value.</param>
    /// <returns>The result of the operation.</returns>
    public static ActionProfile BuildProfile(InputActionSettings settings)
    {
        var profile = new ActionProfile(settings.Actions.Select(action => new ActionDefinition(action.Id, Token<ActionValueKind>(action.Kind), action.Sensitivity, action.Normalize, action.SmoothingSeconds)).ToImmutableArray(), settings.Bindings.Select(binding => new ActionBinding(binding.Id, binding.ActionId, binding.ContextId, Control(binding), new Vector2(binding.ScaleX, binding.ScaleY), binding.PressThreshold, binding.ReleaseThreshold)).ToImmutableArray(), settings.Contexts.Select(context => new InputContext(context.Id, context.Priority, context.Exclusive, context.ParentId, context.Actions.Select(action => new ActionDefinition(action.Id, Token<ActionValueKind>(action.Kind), action.Sensitivity, action.Normalize, action.SmoothingSeconds)).ToImmutableArray())).ToImmutableArray(), settings.Recognitions.Select(recognition => new RecognitionDefinition(recognition.Id, recognition.ContextId, Token<RecognitionKind>(recognition.Kind), recognition.Actions.ToImmutableArray(), TimeSpan.FromSeconds(recognition.WindowSeconds), recognition.TapCount)).ToImmutableArray());
        profile.Validate();
        _ = new ActionInputBuffer(new InputBufferOptions(TimeSpan.FromSeconds(settings.BufferLifetimeSeconds), settings.BufferMaxEntries));
        return profile;
    }

    /// <summary>Creates correction factories from saved calibration values.</summary>
    /// <param name = "settings">The settings value.</param>
    /// <returns>The result of the operation.</returns>
    /// <param name="selectProfile">Maps descriptors to stable user profile names; defaults to device kind names.</param>
    public static Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> BuildCorrections(InputProcessingSettings settings, Func<InputDeviceDescriptor, string>? selectProfile = null)
    {
        _ = new TouchControllerGenerator(settings.TouchRadius, settings.SwipeDistance);
        var profiles = settings.DeviceProfiles.ToDictionary(pair => pair.Key, pair => (Center: new Vector2(pair.Value.CenterX, pair.Value.CenterY), pair.Value.DeadZone, pair.Value.PressureExponent, pair.Value.SmoothingSeconds), StringComparer.Ordinal);
        foreach (KeyValuePair<string, (Vector2 Center, float DeadZone, float PressureExponent, float SmoothingSeconds)> pair in profiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            _ = new AnalogCorrection(pair.Value.Center, pair.Value.DeadZone, pair.Value.PressureExponent);
            if (!float.IsFinite(pair.Value.SmoothingSeconds) || pair.Value.SmoothingSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings));
            }
        }

        return descriptor =>
        {
            string key = selectProfile?.Invoke(descriptor) ?? descriptor.Kind.ToString();
            if (!profiles.TryGetValue(key, out (Vector2 Center, float DeadZone, float PressureExponent, float SmoothingSeconds) profile))
            {
                return [];
            }

            var processors = new List<IDeviceDataProcessor> { new AnalogCorrection(profile.Center, profile.DeadZone, profile.PressureExponent) };
            if (profile.SmoothingSeconds > 0)
            {
                processors.Add(new ExponentialStickSmoothing(profile.SmoothingSeconds));
            }

            return processors;
        };
    }

    /// <summary>Exports stable tokens while excluding transient runtime state.</summary>
    /// <param name = "profile">The profile value.</param>
    /// <param name = "bufferOptions">The bufferOptions value.</param>
    /// <returns>The result of the operation.</returns>
    public static InputActionSettings ToSettings(ActionProfile profile, InputBufferOptions? bufferOptions = null)
    {
        var settings = new InputActionSettings
        {
            Actions = profile.Actions.Select(action => new ActionSettings { Id = action.Id, Kind = action.Kind.ToString(), Sensitivity = action.Sensitivity, Normalize = action.Normalize, SmoothingSeconds = action.SmoothingSeconds }).ToList(),
            Contexts = profile.Contexts.Select(context => new ContextSettings { Id = context.Id, Priority = context.Priority, Exclusive = context.Exclusive, ParentId = context.ParentId, Actions = (context.Actions.IsDefault ? [] : context.Actions).Select(action => new ActionSettings { Id = action.Id, Kind = action.Kind.ToString(), Sensitivity = action.Sensitivity, Normalize = action.Normalize, SmoothingSeconds = action.SmoothingSeconds }).ToList() }).ToList(),
            Recognitions = profile.Recognitions.Select(recognition => new RecognitionSettings { Id = recognition.Id, ContextId = recognition.ContextId, Kind = recognition.Kind.ToString(), Actions = recognition.Actions.ToList(), WindowSeconds = recognition.Window.TotalSeconds, TapCount = recognition.TapCount }).ToList(),
        };
        foreach (ActionBinding binding in profile.Bindings)
        {
            settings.Bindings.Add(new BindingSettings { Id = binding.Id, ActionId = binding.ActionId, ContextId = binding.ContextId, ControlKind = binding.Control.Kind.ToString(), Control = ControlName(binding.Control), ScaleX = binding.Scale.X, ScaleY = binding.Scale.Y, PressThreshold = binding.PressThreshold, ReleaseThreshold = binding.ReleaseThreshold });
        }

        if (bufferOptions is not null)
        {
            settings.BufferLifetimeSeconds = bufferOptions.Lifetime.TotalSeconds;
            settings.BufferMaxEntries = bufferOptions.MaxEntries;
        }

        return settings;
    }

    /// <summary>Checks that saved processing values can construct a correction pipeline.</summary>
    /// <param name = "settings">The settings value.</param>
    /// <returns>The result of the operation.</returns>
    public static bool ValidateProcessing(InputProcessingSettings settings)
    {
        try
        {
            _ = BuildCorrections(settings);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return false;
        }
    }

    /// <summary>Checks that saved values can construct a valid action profile.</summary>
    /// <param name = "settings">The settings value.</param>
    /// <returns>The result of the operation.</returns>
    public static bool ValidateActions(InputActionSettings settings)
    {
        try
        {
            _ = BuildProfile(settings);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        {
            return false;
        }
    }

    private static T Token<T>(string token)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(token) || !Enum.GetNames<T>().Contains(token, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown stable token.", nameof(token));
        }

        return Enum.Parse<T>(token);
    }

    private static InputControl Control(BindingSettings binding)
    {
        InputControlKind kind = Token<InputControlKind>(binding.ControlKind);
        int index = kind switch
        {
            InputControlKind.Key => (int)Token<Key>(binding.Control),
            InputControlKind.MouseButton => (int)Token<MouseButton>(binding.Control),
            InputControlKind.ControllerButton => (int)Token<ControllerButton>(binding.Control),
            InputControlKind.ControllerStick => (int)Token<ControllerStick>(binding.Control),
            InputControlKind.ControllerTrigger => (int)Token<ControllerTrigger>(binding.Control),
            _ => throw new ArgumentException("Unknown control kind."),
        };
        return new InputControl(kind, index);
    }

    private static string ControlName(InputControl control) => control.Kind switch
    {
        InputControlKind.Key => ((Key)control.Index).ToString(),
        InputControlKind.MouseButton => ((MouseButton)control.Index).ToString(),
        InputControlKind.ControllerButton => ((ControllerButton)control.Index).ToString(),
        InputControlKind.ControllerStick => ((ControllerStick)control.Index).ToString(),
        InputControlKind.ControllerTrigger => ((ControllerTrigger)control.Index).ToString(),
        _ => throw new ArgumentException("Unknown control kind."),
    };
}
