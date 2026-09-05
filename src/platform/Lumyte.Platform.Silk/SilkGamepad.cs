using System.Numerics;

using Lumyte.Input;
using NativeGamepad = Silk.NET.Input.IGamepad;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkGamepad : IGamepad, IDisposable
{
    private readonly NativeGamepad native;
    private GamepadState state;

    internal SilkGamepad(NativeGamepad native)
    {
        this.native = native;
        Id = new($"silk:{native.Index}:{native.Name}");
        state = ReadState();
        native.ButtonDown += OnStateChanged;
        native.ButtonUp += OnStateChanged;
        native.ThumbstickMoved += OnStateChanged;
        native.TriggerMoved += OnStateChanged;
    }

    public event EventHandler<GamepadStateChangedEventArgs>? StateChanged;

    public NativeGamepad Native => native;

    public GamepadId Id { get; }

    public string Name => native.Name;

    public GamepadState State => state;

    public bool SupportsVibration => native.VibrationMotors.Count > 0;

    public void SetVibration(GamepadVibration vibration)
    {
        GamepadVibration clamped = vibration.Clamp();
        for (int index = 0; index < native.VibrationMotors.Count; index++)
        {
            native.VibrationMotors[index].Speed = index == 0
                ? clamped.LowFrequency
                : clamped.HighFrequency;
        }
    }

    public void Dispose()
    {
        native.ButtonDown -= OnStateChanged;
        native.ButtonUp -= OnStateChanged;
        native.ThumbstickMoved -= OnStateChanged;
        native.TriggerMoved -= OnStateChanged;
        SetVibration(default);
    }

    private void OnStateChanged(NativeGamepad _, Silk.NET.Input.Button button) => Update();

    private void OnStateChanged(NativeGamepad _, Silk.NET.Input.Thumbstick thumbstick) => Update();

    private void OnStateChanged(NativeGamepad _, Silk.NET.Input.Trigger trigger) => Update();

    private void Update()
    {
        GamepadState previous = state;
        state = ReadState();
        if (previous != state)
        {
            StateChanged?.Invoke(this, new(previous, state));
        }
    }

    private GamepadState ReadState()
    {
        GamepadButtons buttons = GamepadButtons.None;
        foreach (Silk.NET.Input.Button button in native.Buttons)
        {
            if (button.Pressed)
            {
                buttons |= SilkInputConversions.ToLumyte(button.Name);
            }
        }

        Vector2 leftStick = native.Thumbsticks.Count > 0
            ? new(native.Thumbsticks[0].X, native.Thumbsticks[0].Y)
            : Vector2.Zero;
        Vector2 rightStick = native.Thumbsticks.Count > 1
            ? new(native.Thumbsticks[1].X, native.Thumbsticks[1].Y)
            : Vector2.Zero;
        float leftTrigger = native.Triggers.Count > 0
            ? native.Triggers[0].Position
            : 0;
        float rightTrigger = native.Triggers.Count > 1
            ? native.Triggers[1].Position
            : 0;
        return new(buttons, leftStick, rightStick, leftTrigger, rightTrigger);
    }
}
