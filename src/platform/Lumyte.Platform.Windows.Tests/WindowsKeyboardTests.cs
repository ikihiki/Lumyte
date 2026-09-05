using Lumyte.Input;
using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsKeyboardTests
{
    [Fact]
    public void KeyChangesUpdateStateBeforeNotification()
    {
        var keyboard = new WindowsKeyboard();
        bool? stateDuringNotification = null;
        keyboard.KeyChanged += (_, eventArgs) =>
            stateDuringNotification = keyboard.IsKeyPressed(eventArgs.Key);

        keyboard.ChangeKey(Key.A, true, false);

        Assert.True(stateDuringNotification);
        Assert.True(keyboard.IsKeyPressed(Key.A));

        keyboard.ChangeKey(Key.A, false, false);

        Assert.False(stateDuringNotification);
        Assert.False(keyboard.IsKeyPressed(Key.A));
    }

    [Fact]
    public void RepeatedKeyIsReportedWithoutChangingItsMeaning()
    {
        var keyboard = new WindowsKeyboard();
        KeyChangedEventArgs? change = null;
        keyboard.KeyChanged += (_, eventArgs) => change = eventArgs;
        keyboard.ChangeKey(Key.Enter, true, false);

        keyboard.ChangeKey(Key.Enter, true, true);

        Assert.NotNull(change);
        Assert.Equal(Key.Enter, change.Key);
        Assert.True(change.IsPressed);
        Assert.True(change.IsRepeat);
        Assert.True(keyboard.IsKeyPressed(Key.Enter));
    }
}
