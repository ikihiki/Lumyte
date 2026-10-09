using System.Numerics;
using Lumyte.Settings;

namespace Lumyte.Input;

/// <summary>Applies a captured settings revision without modifying original Input records.</summary>
public sealed class InputSettingsProcessor
{
    private readonly IEditableOptions<InputSettings> _settings;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private SettingsSnapshot<InputSettings> _snapshot;

    /// <summary>Initializes a new instance of the <see cref="InputSettingsProcessor"/> class.</summary>
    /// <param name="settings">The module's persistent settings.</param>
    public InputSettingsProcessor(IEditableOptions<InputSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _snapshot = settings.Current;
    }

    /// <summary>Gets the applied revision.</summary>
    public long Revision => _snapshot.Revision;

    /// <summary>Captures settings once at the frame boundary on the owning thread.</summary>
    public void Refresh()
    {
        RequireThread();
        if (_settings.Revision != _snapshot.Revision)
        {
            _snapshot = _settings.Current;
        }
    }

    /// <summary>Gets a detached copy of the physical bindings for an action.</summary>
    /// <param name="context">The context ID.</param>
    /// <param name="action">The action ID.</param>
    /// <returns>The bindings, or an empty array.</returns>
    public string[] GetBindings(string context, string action)
    {
        RequireThread();
        return _snapshot.Value.Bindings.TryGetValue(context, out Dictionary<string, string[]>? actions) && actions.TryGetValue(action, out string[]? bindings) ? bindings.ToArray() : [];
    }

    /// <summary>Transforms a normalized stick position using radial thresholds.</summary>
    /// <param name="stick">The stick.</param>
    /// <param name="value">The original normalized position.</param>
    /// <returns>The transformed position.</returns>
    public Vector2 ApplyStick(ControllerStick stick, Vector2 value)
    {
        RequireThread();
        DeviceState.CheckEnum(stick);
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X is < -1 or > 1 || value.Y is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        DeadZoneSettings zone = stick == ControllerStick.Left ? _snapshot.Value.LeftStick : _snapshot.Value.RightStick;
        float length = value.Length();
        return length == 0 ? Vector2.Zero : (value / length) * Math.Clamp((length - zone.Inner) / (zone.Outer - zone.Inner), 0, 1);
    }

    /// <summary>Transforms a normalized trigger using its configured thresholds.</summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="value">The original zero-to-one value.</param>
    /// <returns>The transformed value.</returns>
    public float ApplyTrigger(ControllerTrigger trigger, float value)
    {
        RequireThread();
        DeviceState.CheckEnum(trigger);
        if (!float.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        DeadZoneSettings zone = trigger == ControllerTrigger.Left ? _snapshot.Value.LeftTrigger : _snapshot.Value.RightTrigger;
        return Math.Clamp((value - zone.Inner) / (zone.Outer - zone.Inner), 0, 1);
    }

    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Use the Input settings processor on its owning thread.");
        }
    }
}
