using System.Numerics;

using Lumyte.Input;
using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsTouchscreenTests
{
    [Fact]
    public void TracksMultipleTouchesIndependently()
    {
        var touchscreen = new WindowsTouchscreen();

        touchscreen.ChangeTouch(10, new(5, 7), TouchPhase.Began, 0.25f);
        touchscreen.ChangeTouch(20, new(9, 11), TouchPhase.Began, null);
        touchscreen.ChangeTouch(10, new(8, 12), TouchPhase.Moved, 0.5f);

        Assert.Collection(
            touchscreen.ActiveTouches.OrderBy(touch => touch.Id),
            first => Assert.Equal(
                new TouchPoint(10, new(8, 12), new(3, 5), TouchPhase.Moved, 0.5f),
                first),
            second => Assert.Equal(
                new TouchPoint(20, new(9, 11), Vector2.Zero, TouchPhase.Began, null),
                second));
    }

    [Fact]
    public void EndingTouchRemovesItBeforeNotification()
    {
        var touchscreen = new WindowsTouchscreen();
        bool? wasActiveDuringNotification = null;
        touchscreen.ChangeTouch(7, new(2, 3), TouchPhase.Began, null);
        touchscreen.TouchChanged += (_, eventArgs) =>
            wasActiveDuringNotification = touchscreen.ActiveTouches.Any(
                touch => touch.Id == eventArgs.Touch.Id);

        touchscreen.ChangeTouch(7, new(4, 6), TouchPhase.Ended, null);

        Assert.False(wasActiveDuringNotification);
        Assert.Empty(touchscreen.ActiveTouches);
    }

    [Fact]
    public void CancellingAllEndsEveryActiveTouch()
    {
        var touchscreen = new WindowsTouchscreen();
        List<TouchPoint> changes = [];
        touchscreen.ChangeTouch(1, new(1, 2), TouchPhase.Began, null);
        touchscreen.ChangeTouch(2, new(3, 4), TouchPhase.Began, 0.75f);
        touchscreen.TouchChanged += (_, eventArgs) => changes.Add(eventArgs.Touch);

        touchscreen.CancelAll();

        Assert.Empty(touchscreen.ActiveTouches);
        Assert.Collection(
            changes.OrderBy(touch => touch.Id),
            first => Assert.Equal(TouchPhase.Cancelled, first.Phase),
            second => Assert.Equal(TouchPhase.Cancelled, second.Phase));
    }
}
