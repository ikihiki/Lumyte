using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

internal sealed class SwipeGestureRecognizer : GestureRecognizer
{
    private readonly SwipeGesture gesture;
    private readonly Duration maximumDuration;
    private readonly ContactTrack[] contacts;
    private int activeContactCount;
    private int contactCount;
    private bool invalid;

    public SwipeGestureRecognizer(SwipeGesture gesture)
    {
        this.gesture = gesture;
        maximumDuration = Duration.FromTimeSpan(gesture.MaximumDuration);
        contacts = new ContactTrack[gesture.FingerCount];
    }

    public override GestureRecognition? Process(in GestureInput input)
    {
        switch (input.Touch.Phase)
        {
            case TouchPhase.Began:
                Begin(input);
                return null;
            case TouchPhase.Moved:
                Update(input);
                return null;
            case TouchPhase.Ended:
                return End(input);
            case TouchPhase.Cancelled:
                Reset();
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    public override void Reset()
    {
        activeContactCount = 0;
        contactCount = 0;
        invalid = false;
    }

    private void Begin(in GestureInput input)
    {
        activeContactCount++;
        if (contactCount == contacts.Length)
        {
            invalid = true;
            return;
        }

        contacts[contactCount++] = new(
            input.Touch.Id,
            input.Touch.Position,
            input.Touch.Position,
            input.Timestamp);
    }

    private void Update(in GestureInput input)
    {
        int index = FindContact(input.Touch.Id);
        if (index >= 0)
        {
            contacts[index] = contacts[index] with { Position = input.Touch.Position };
        }
    }

    private GestureRecognition? End(in GestureInput input)
    {
        Update(input);
        activeContactCount = Math.Max(0, activeContactCount - 1);
        if (activeContactCount != 0)
        {
            return null;
        }

        GestureRecognition? result = !invalid && contactCount == contacts.Length
            ? Recognize(input.Timestamp)
            : null;
        Reset();
        return result;
    }

    private GestureRecognition? Recognize(TimePoint timestamp)
    {
        Vector2 startCentroid = default;
        Vector2 endCentroid = default;
        TimePoint startTime = contacts[0].StartTime;
        foreach (ContactTrack contact in contacts)
        {
            startCentroid += contact.Start;
            endCentroid += contact.Position;
            if (contact.StartTime < startTime)
            {
                startTime = contact.StartTime;
            }
        }

        startCentroid /= contacts.Length;
        endCentroid /= contacts.Length;
        Vector2 delta = endCentroid - startCentroid;
        SwipeDirection direction = GetDirection(delta);
        Duration duration = timestamp - startTime;
        float seconds = (float)duration.TotalSeconds;
        Vector2 velocity = seconds > 0 ? delta / seconds : Vector2.Zero;
        if (duration > maximumDuration
            || delta.Length() < gesture.MinimumDistance
            || velocity.Length() < gesture.MinimumVelocity
            || (gesture.Direction != SwipeDirection.Any && gesture.Direction != direction))
        {
            return null;
        }

        foreach (ContactTrack contact in contacts)
        {
            Vector2 contactDelta = contact.Position - contact.Start;
            if (contactDelta.Length() < gesture.MinimumDistance
                || GetDirection(contactDelta) != direction)
            {
                return null;
            }
        }

        return new(
            GestureKind.Swipe,
            Specificity: gesture.FingerCount + 1,
            Delta: delta,
            Velocity: velocity,
            Duration: duration,
            FingerCount: gesture.FingerCount);
    }

    private int FindContact(long id)
    {
        for (int index = 0; index < contactCount; index++)
        {
            if (contacts[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static SwipeDirection GetDirection(Vector2 delta) =>
        Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? delta.X >= 0 ? SwipeDirection.Right : SwipeDirection.Left
            : delta.Y >= 0 ? SwipeDirection.Down : SwipeDirection.Up;

    private readonly record struct ContactTrack(
        long Id,
        Vector2 Start,
        Vector2 Position,
        TimePoint StartTime);
}
