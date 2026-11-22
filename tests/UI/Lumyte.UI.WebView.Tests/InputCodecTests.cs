using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;
using WebCompose = Lumyte.UI.WebView.Compose;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Checks fixed native input bodies and primary-only routed ownership.</summary>
public sealed class InputCodecTests
{
    /// <summary>Checks local logical coordinates use only the supplied cached scale.</summary>
    /// <param name="phase">The routed mouse phase.</param>
    /// <param name="type">The expected native transition.</param>
    [Theory]
    [InlineData(UiPointerPhase.Move, 1)]
    [InlineData(UiPointerPhase.Down, 3)]
    [InlineData(UiPointerPhase.Up, 4)]
    public void MouseUsesLocalPixels(UiPointerPhase phase, int type)
    {
        byte[] packet = Encode(new UiPointerInput(1, phase, new(900, 800)), new(10.25f, -2.5f), 2);
        Assert.Equal(48, packet.Length);
        Assert.Equal(type, I32(packet, 0));
        Assert.Equal(21, I32(packet, 8));
        Assert.Equal(-5, I32(packet, 12));
        Assert.Equal(123, BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(40)));
        Assert.All(packet[16..40], value => Assert.Equal((byte)0, value));
    }

    /// <summary>Checks both wheel axes reverse UI content-end signs and use owner scale.</summary>
    [Fact]
    public void WheelReversesBothAxes()
    {
        byte[] packet = Encode(new UiWheelInput(new(700, 800), new(3, -4)), new(2, 5), 1.5f);
        Assert.Equal(5, I32(packet, 0));
        Assert.Equal(3, I32(packet, 8));
        Assert.Equal(8, I32(packet, 12));
        Assert.Equal(-5, I32(packet, 16));
        Assert.Equal(6, I32(packet, 20));
    }

    /// <summary>Checks every declared physical key has an explicit native mapping except Unknown.</summary>
    [Fact]
    public void PhysicalMappingCoversDeclaredKeys()
    {
        foreach (Key key in Enum.GetValues<Key>())
        {
            Assert.Equal(key != Key.Unknown, CefInputCodec.MapKey(key, out int vk, out int native));
            if (key != Key.Unknown)
            {
                Assert.InRange(vk, 1, 255);
                Assert.InRange(native, 8, 255);
            }
        }

        Assert.False(CefInputCodec.MapKey((Key)int.MaxValue, out _, out _));
    }

    /// <summary>Checks CEF modifiers, native X11 hardware code, repeat, and physical/text separation.</summary>
    [Fact]
    public void PhysicalKeysCarryModifiersButNeverCharacters()
    {
        byte[] packet = Encode(new UiKeyInput(Key.A, true, true, UiModifiers.Shift | UiModifiers.Control | UiModifiers.Alt | UiModifiers.Meta));
        Assert.Equal(6, I32(packet, 0));
        Assert.Equal(2 | 4 | 8 | 128 | (1 << 13), I32(packet, 4));
        Assert.Equal(65, I32(packet, 24));
        Assert.Equal(38, I32(packet, 28));
        Assert.Equal(0, I32(packet, 32));
        Assert.Equal(3, I32(packet, 36));
        Assert.Equal(7, I32(Encode(new UiKeyInput(Key.A, false)), 0));
        Assert.Equal(1 << 11, I32(Encode(new UiKeyInput(Key.RightShift, true)), 4));
        Assert.Equal(1 << 9, I32(Encode(new UiKeyInput(Key.NumpadEnter, true)), 4));
    }

    /// <summary>Checks committed Unicode produces ordered UTF16 units without physical key events.</summary>
    [Fact]
    public void CommittedTextPreservesSupplementaryUnicode()
    {
        string text = string.Concat(Enumerable.Repeat("日本語😀", 32));
        Assert.True(CefInputCodec.TryEncode(new UiCommittedTextInput(text), null, 1, 123, out byte[][] packets));
        byte[] packet = Assert.Single(packets);
        Assert.Equal(11, I32(packet, 0));
        Assert.Equal(text, new System.Text.UTF8Encoding(false, true).GetString(packet.AsSpan(48)));
        Assert.Equal(123, BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(40)));
        Assert.All(packet[4..40], value => Assert.Equal((byte)0, value));

        Assert.True(CefInputCodec.TryEncode(new UiCommittedTextInput(string.Empty), null, 1, 123, out packets));
        Assert.Empty(packets);
    }

    /// <summary>Checks malformed surrogate input rejects the entire commit before emitting packets.</summary>
    [Fact]
    public void MalformedUnicodeIsRejectedAtomically()
    {
        foreach (string text in new[] { "ok\uD800", "\uDC00", "\uD800x", "\uDC00\uD800" })
        {
            Assert.False(CefInputCodec.TryEncode(new UiCommittedTextInput(text), null, 1, 123, out byte[][] packets));
            Assert.Empty(packets);
        }
    }

    /// <summary>Checks cancellation uses the native release-all path even without coordinates.</summary>
    [Fact]
    public void CancellationAndFocusUseNativeReleasePath()
    {
        Assert.Equal(9, I32(CefInputCodec.Focus(true, 123), 0));
        Assert.Equal(10, I32(CefInputCodec.Focus(false, 123), 0));
        Assert.Equal(10, I32(Encode(new UiKeyCancelInput(Key.A)), 0));
        Assert.Equal(10, I32(Encode(new UiFocusLostInput()), 0));
        Assert.Equal(10, I32(Encode(new UiPointerInput(1, UiPointerPhase.Cancel, default)), 0));
    }

    /// <summary>Checks unsupported pointer sources never become mouse events.</summary>
    /// <param name="device">The unsupported input device.</param>
    [Theory]
    [InlineData(UiPointerDevice.Touch)]
    [InlineData(UiPointerDevice.Pen)]
    public void UnsupportedPointerNeverFocusesOrCaptures(UiPointerDevice device)
    {
        var host = new RecordingHost();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ => { }) { Host = host };
        using var surface = new UiSurface(WebCompose.WebView(session: session));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        var pointer = new UiPointerInput(1, UiPointerPhase.Down, new(10, 20)) { Device = device };
        Assert.False(surface.Dispatch(pointer));
        Assert.Null(surface.FocusedElement);
        Assert.Empty(host.Inputs);
        Assert.False(CefInputCodec.TryEncode(pointer, new(10, 20), 1, 123, out byte[][] packets));
        Assert.Empty(packets);
    }

    /// <summary>Checks focus loss reaches a captured physical series as cancellation.</summary>
    [Fact]
    public void PhysicalKeyDownCapturesItsCancellation()
    {
        var host = new RecordingHost();
        var session = new UiWebViewSession(new("https://example.invalid/"), () => { }, _ => { }) { Host = host };
        using var surface = new UiSurface(WebCompose.WebView(session: session));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(10, 20))));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(10, 20))));
        Assert.True(surface.Dispatch(new UiKeyInput(Key.A, true)));
        surface.Dispatch(new UiFocusLostInput());
        Assert.Contains(host.Inputs, input => input is UiKeyCancelInput { Key: Key.A });
    }

    /// <summary>Checks nonfinite, overflowing and missing positional input is rejected.</summary>
    [Fact]
    public void InvalidCoordinatesAndScaleAreRejected()
    {
        var input = new UiPointerInput(1, UiPointerPhase.Move, default);
        Assert.False(CefInputCodec.TryEncode(input, null, 1, 123, out _));
        Assert.False(CefInputCodec.TryEncode(input, new(float.NaN, 1), 1, 123, out _));
        Assert.False(CefInputCodec.TryEncode(input, new(float.MaxValue, 1), 2, 123, out _));
        Assert.False(CefInputCodec.TryEncode(input, Vector2.One, 0, 123, out _));
        Assert.False(CefInputCodec.TryEncode(input, Vector2.One, float.PositiveInfinity, 123, out _));
    }

    /// <summary>Oversize committed text is rejected atomically before producing native events.</summary>
    [Fact]
    public void CommittedTextBatchIsBounded()
    {
        Assert.False(CefInputCodec.TryEncode(new UiCommittedTextInput(new string('a', 1025)), null, 1, 1, out byte[][] packets));
        Assert.Empty(packets);
    }

    /// <summary>Small signed wheel deltas accumulate rather than disappearing, without a second wheel-notch multiplier.</summary>
    [Fact]
    public void FractionalWheelPixelsAccumulate()
    {
        Vector2 residual = default;
        int dx = 0;
        int dy = 0;
        for (int i = 0; i < 8; i++)
        {
            Assert.True(CefInputCodec.TryEncodeWheel(new(Vector2.Zero, new(0.125f, -0.125f)), Vector2.Zero, 2, 1, ref residual, out byte[][] packets));
            foreach (byte[] packet in packets)
            {
                dx += I32(packet, 16);
                dy += I32(packet, 20);
            }
        }

        Assert.Equal(-2, dx);
        Assert.Equal(2, dy);
        Assert.Equal(Vector2.Zero, residual);
        Assert.False(CefInputCodec.TryEncodeWheel(new(Vector2.Zero, new(float.NaN, 0)), Vector2.Zero, 2, 1, ref residual, out _));
        Assert.Equal(Vector2.Zero, residual);
    }

    private static byte[] Encode(UiInput input, Vector2? position = null, float scale = 1)
    {
        Assert.True(CefInputCodec.TryEncode(input, position, scale, 123, out byte[][] packets));
        return Assert.Single(packets);
    }

    private static int I32(byte[] packet, int offset) => BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(offset));

    private sealed class RecordingHost : IWebViewHost
    {
        internal List<UiInput> Inputs { get; } = [];

        public void ValidateSurface(UiSurface surface)
        {
        }

        public void Viewport(Vector2 logicalSize, bool visible)
        {
        }

        public void Input(UiInput input, Vector2? localPosition) => Inputs.Add(input);

        public void Focus(bool focused)
        {
        }

        public void Detach()
        {
        }
    }
}
