using System.Numerics;
using System.Diagnostics;

using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class GestureRuntime : IDisposable
{
    private readonly GestureArena arena;
    private readonly IMonotonicClock clock;
    private readonly ITouchscreen touchscreen;
    private IDisposable? ownedSource;
    private readonly Dictionary<long, TouchTrack> touches = [];
    private float? pinchStartDistance;
    private bool disposed;

    public GestureRuntime(
        ITouchscreen touchscreen,
        InteractionContext context,
        IMonotonicClock clock,
        params ReadOnlySpan<GestureMap> maps)
    {
        this.touchscreen = touchscreen ?? throw new ArgumentNullException(nameof(touchscreen));
        ArgumentNullException.ThrowIfNull(context);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        arena = new(context, maps);
        touchscreen.TouchChanged += OnTouchChanged;
    }

    public GestureRuntime(
        IMouse mouse,
        InteractionContext context,
        IMonotonicClock clock,
        MouseButton button = MouseButton.Left,
        params ReadOnlySpan<GestureMap> maps)
        : this(new MouseGestureSource(mouse, button), context, clock, maps)
    {
        ownedSource = (IDisposable)touchscreen;
    }

    public event EventHandler<GestureRecognizedEventArgs>? Recognized;

    public void Cancel()
    {
        touches.Clear();
        pinchStartDistance = null;
        arena.Reset();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        touchscreen.TouchChanged -= OnTouchChanged;
        ownedSource?.Dispose();
        ownedSource = null;
        Cancel();
    }

    private void OnTouchChanged(object? sender, TouchChangedEventArgs eventArgs)
    {
        TouchPoint touch = eventArgs.Touch;
        TimePoint now = clock.Now;
        TouchTrack track;
        switch (touch.Phase)
        {
            case TouchPhase.Began:
                track = new(touch.Position, touch.Position, 0, now);
                touches[touch.Id] = track;
                if (touches.Count == 2)
                {
                    pinchStartDistance = GetTouchDistance();
                }

                break;
            case TouchPhase.Moved:
                if (!touches.TryGetValue(touch.Id, out track))
                {
                    return;
                }

                track = track with
                {
                    Position = touch.Position,
                    MaximumDistance = Math.Max(
                        track.MaximumDistance,
                        Vector2.Distance(track.Start, touch.Position)),
                };
                touches[touch.Id] = track;
                break;
            case TouchPhase.Ended:
            case TouchPhase.Cancelled:
                if (!touches.TryGetValue(touch.Id, out track))
                {
                    return;
                }

                track = track with
                {
                    Position = touch.Position,
                    MaximumDistance = Math.Max(
                        track.MaximumDistance,
                        Vector2.Distance(track.Start, touch.Position)),
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(eventArgs));
        }

        float? pinchScale = GetPinchScale();
        var input = new GestureInput(
            touch,
            track.Start,
            track.MaximumDistance,
            now - track.StartTime,
            now,
            pinchScale);
        if (arena.Process(input) is ArenaMatch match)
        {
            GestureRecognition recognition = match.Recognition;
            using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
                "GestureRuntime.Recognize");
            activity?.SetTag("interaction.gesture.kind", recognition.Kind.ToString());
            activity?.SetTag("interaction.gesture.intent", match.Binding.Intent.Id);
            activity?.SetTag("interaction.gesture.finger_count", recognition.FingerCount);
            InteractionDiagnostics.GesturesRecognized.Add(
                1,
                new("gesture_kind", recognition.Kind.ToString()),
                new("value_type", GetValueTypeName(match.Binding.ValueType)));
            Recognized?.Invoke(
                this,
                new(
                    match.Binding.Intent,
                    recognition.Kind,
                    recognition.Delta,
                    recognition.Scale,
                    recognition.Velocity,
                    recognition.Duration,
                    recognition.FingerCount));
        }

        if (touch.Phase is TouchPhase.Ended or TouchPhase.Cancelled)
        {
            touches.Remove(touch.Id);
            pinchStartDistance = touches.Count == 2 ? GetTouchDistance() : null;
        }
    }

    private static string GetValueTypeName(Type type) => type == typeof(bool)
        ? "button"
        : type == typeof(float)
            ? "scalar"
            : type == typeof(Vector2) ? "vector2" : "other";

    private float? GetPinchScale()
    {
        if (touches.Count != 2 || pinchStartDistance is not > 0)
        {
            return null;
        }

        return GetTouchDistance() / pinchStartDistance.Value;
    }

    private float GetTouchDistance()
    {
        using Dictionary<long, TouchTrack>.ValueCollection.Enumerator enumerator =
            touches.Values.GetEnumerator();
        enumerator.MoveNext();
        Vector2 first = enumerator.Current.Position;
        enumerator.MoveNext();
        return Vector2.Distance(first, enumerator.Current.Position);
    }

    private sealed class GestureArena
    {
        private readonly InteractionContext context;
        private readonly RecognizerEntry[] entries;

        public GestureArena(InteractionContext context, ReadOnlySpan<GestureMap> maps)
        {
            this.context = context;
            var compiled = new List<RecognizerEntry>();
            foreach (GestureMap map in maps)
            {
                foreach (GestureBinding binding in map.Bindings)
                {
                    compiled.Add(new(
                        binding,
                        binding.CreateRecognizer(),
                        map.When,
                        map.Priority));
                }
            }

            entries = [.. compiled];
        }

        public ArenaMatch? Process(in GestureInput input)
        {
            RecognizerEntry? best = null;
            GestureRecognition bestRecognition = default;
            bool conflict = false;
            foreach (RecognizerEntry entry in entries)
            {
                if (!entry.When.Evaluate(context))
                {
                    continue;
                }

                GestureRecognition? candidate = entry.Recognizer.Process(input);
                if (candidate is not GestureRecognition recognition)
                {
                    continue;
                }

                if (best is null
                    || entry.Priority > best.Priority
                    || (entry.Priority == best.Priority
                        && recognition.Specificity > bestRecognition.Specificity))
                {
                    best = entry;
                    bestRecognition = recognition;
                    conflict = false;
                }
                else if (entry.Priority == best.Priority
                    && recognition.Specificity == bestRecognition.Specificity)
                {
                    conflict = true;
                }
            }

            return best is not null && !conflict
                ? new(best.Binding, bestRecognition)
                : null;
        }

        public void Reset()
        {
            foreach (RecognizerEntry entry in entries)
            {
                entry.Recognizer.Reset();
            }
        }
    }

    private sealed record RecognizerEntry(
        GestureBinding Binding,
        GestureRecognizer Recognizer,
        ContextCondition When,
        int Priority);

    private readonly record struct ArenaMatch(
        GestureBinding Binding,
        GestureRecognition Recognition);

    private readonly record struct TouchTrack(
        Vector2 Start,
        Vector2 Position,
        float MaximumDistance,
        TimePoint StartTime);
}
