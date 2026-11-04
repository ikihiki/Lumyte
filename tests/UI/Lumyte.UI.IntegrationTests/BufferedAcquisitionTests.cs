using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Internal;
using Lumyte.Platform.Tests;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks the acquisition buffer used by every standard platform.</summary>
public sealed class BufferedAcquisitionTests
{
    /// <summary>Checks source acquisition order, same-cause coordinates, and stable acknowledgments.</summary>
    [Fact]
    public void AcquisitionBufferPreservesCrossDeviceOrderAndSameCausePairs()
    {
        using var platform = new TestPlatform();
        var window = (TestWindow)platform.CreateWindow(new());
        using var source = new BufferedInputSource(() => { }, _ => { }, () => platform.Windows, "test");
        using var input = new InputSystem([source]);
        Assert.True(source.TryGetInputWindowId(window.Id, out InputWindowId origin));
        source.Keyboard(window.Id, new FocusData(true));
        source.Keyboard(window.Id, new KeyData(Key.A, true, false));
        ulong capture = source.BeginAcquisition();
        source.Mouse(window.Id, new MouseMoveData(new(1, 2)), capture);
        source.Mouse(window.Id, new MouseButtonData(MouseButton.Left, true), capture);
        source.Keyboard(window.Id, new KeyData(Key.B, true, false));
        var events = new List<InputRecord>();
        input.Recorded += events.Add;
        InputEventBatch batch = Assert.IsType<InputEventBatch>(source.PeekEvents());
        Assert.Same(batch, source.PeekEvents());
        input.Update();
        InputRecord[] ordered = events.Where(item => item.Capture is not null).ToArray();
        Assert.IsType<KeyData>(ordered[^4].Data);
        Assert.IsType<MouseMoveData>(ordered[^3].Data);
        Assert.IsType<MouseButtonData>(ordered[^2].Data);
        Assert.IsType<KeyData>(ordered[^1].Data);
        Assert.Equal(ordered[^3].Capture, ordered[^2].Capture);
        Assert.All(ordered, item => Assert.Equal(origin, item.WindowId));
        source.CommitEvents(batch.Id);
        Assert.Null(source.PeekEvents());
        Assert.Throws<ArgumentException>(() => source.CommitEvents(batch.Id + 1));
    }

    /// <summary>Checks closed native identity is retired even when the same window still exists during GPU retirement.</summary>
    [Fact]
    public void ClosingWindowRetiresIdentityAndRecordsEveryDeviceScope()
    {
        using var platform = new TestPlatform();
        var window = (TestWindow)platform.CreateWindow(new());
        using var source = new BufferedInputSource(() => { }, _ => { }, () => platform.Windows, "test");
        using var input = new InputSystem([source]);
        Assert.True(source.TryGetInputWindowId(window.Id, out InputWindowId original));
        input.Update();
        source.Close(window.Id);
        Assert.False(source.TryGetInputWindowId(window.Id, out _));
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        input.Update();
        InputRecord[] closes = records.Where(item => item.Data is WindowInputClosedData).ToArray();
        Assert.Equal(2, closes.Length);
        Assert.All(closes, item => Assert.Equal(original, item.WindowId));
        var replacement = (TestWindow)platform.CreateWindow(new());
        Assert.True(source.TryGetInputWindowId(replacement.Id, out InputWindowId next));
        Assert.NotEqual(original, next);
    }
}
