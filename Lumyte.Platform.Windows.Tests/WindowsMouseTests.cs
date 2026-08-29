using System.Numerics;

using Lumyte.Input;
using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsMouseTests
{
    [Fact]
    public void MovementReportsPositionAndDelta()
    {
        var mouse = new WindowsMouse();
        MouseMovedEventArgs? movement = null;
        mouse.Moved += (_, eventArgs) => movement = eventArgs;
        mouse.Move(new(10, 20));

        mouse.Move(new(13, 25));

        Assert.NotNull(movement);
        Assert.Equal(new Vector2(13, 25), movement.Position);
        Assert.Equal(new Vector2(3, 5), movement.Delta);
        Assert.Equal(movement.Position, mouse.Position);
    }

    [Fact]
    public void ButtonChangesUpdateStateBeforeNotification()
    {
        var mouse = new WindowsMouse();
        bool? stateDuringNotification = null;
        mouse.ButtonChanged += (_, eventArgs) =>
            stateDuringNotification = mouse.IsButtonPressed(eventArgs.Button);

        mouse.ChangeButton(MouseButton.Button4, true, new(4, 8));

        Assert.True(stateDuringNotification);
        Assert.True(mouse.IsButtonPressed(MouseButton.Button4));

        mouse.ChangeButton(MouseButton.Button4, false, new(5, 9));

        Assert.False(stateDuringNotification);
        Assert.False(mouse.IsButtonPressed(MouseButton.Button4));
    }

    [Fact]
    public void WheelReportsBothAxes()
    {
        var mouse = new WindowsMouse();
        MouseWheelChangedEventArgs? change = null;
        mouse.WheelChanged += (_, eventArgs) => change = eventArgs;

        mouse.ChangeWheel(new(1, -2));

        Assert.NotNull(change);
        Assert.Equal(new Vector2(1, -2), change.Delta);
    }
}
