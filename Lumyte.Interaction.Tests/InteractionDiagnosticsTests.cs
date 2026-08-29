using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

using Lumyte.Core.Time;
using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class InteractionDiagnosticsTests
{
    [Fact]
    public void ActionAndGestureMetricsDescribeObservableInputOutcomes()
    {
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = CreateMeterListener(measurements);
        var jump = new InputAction<bool>("game.jump");
        ActionMap actions = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var keyboard = new VirtualKeyboard();
        using var actionRuntime = new ActionRuntime(
            keyboard,
            new InteractionContext(),
            actions);
        var select = new Command("editor.select");
        GestureMap gestures = GestureMap("Viewport")[new TapGesture(select)];
        var touchscreen = new VirtualTouchscreen();
        using var gestureRuntime = new GestureRuntime(
            touchscreen,
            new InteractionContext(),
            new ManualClock(),
            gestures);

        keyboard.Press(Key.Space);
        keyboard.Release(Key.Space);
        touchscreen.Begin(1, new(20, 30));
        touchscreen.End(1, new(20, 30));

        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == "lumyte.interaction.action.phase_changes"
                && measurement.HasTag("phase", "Performed")
                && measurement.HasTag("value_type", "button"));
        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == "lumyte.interaction.gestures.recognized"
                && measurement.HasTag("gesture_kind", "Tap")
                && measurement.HasTag("value_type", "button"));
    }

    [Fact]
    public void KeybindingInvocationProducesAnActivityAndMetric()
    {
        Activity? stopped = null;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == InteractionDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "KeybindingRuntime.Invoke")
                {
                    stopped = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
        var measurements = new ConcurrentQueue<Measurement>();
        using var meterListener = CreateMeterListener(measurements);
        var save = new Command("editor.save");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(save, KeyChordParser.Parse("ctrl+s"))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new KeybindingRuntime(
            keyboard,
            new InteractionContext(),
            new ManualClock(),
            map);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.S);

        Assert.NotNull(stopped);
        Assert.Equal("KeybindingRuntime.Invoke", stopped.OperationName);
        Assert.Equal("editor.save", stopped.GetTagItem("interaction.command.id"));
        Assert.Contains(
            measurements,
            measurement =>
                measurement.Name == "lumyte.interaction.keybinding.resolutions"
                && measurement.HasTag("outcome", "invoked"));
    }

    private static MeterListener CreateMeterListener(
        ConcurrentQueue<Measurement> measurements)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == InteractionDiagnostics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) =>
                measurements.Enqueue(new(instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private sealed record Measurement(
        string Name,
        long Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags)
    {
        public bool HasTag(string name, object value) =>
            Tags.Any(tag => tag.Key == name && Equals(tag.Value, value));
    }
}
