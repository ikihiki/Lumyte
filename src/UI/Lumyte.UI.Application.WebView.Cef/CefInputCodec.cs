using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Encodes owner-routed input without querying a window or synthesizing text from keys.</summary>
internal static class CefInputCodec
{
    internal const int BodySize = 48;

    internal static byte[] Focus(bool focused, long documentEpoch) => Packet(focused ? 9u : 10u, documentEpoch);

    internal static bool TryEncode(UiInput input, Vector2? localPosition, float scale, long documentEpoch, out byte[][] packets)
    {
        packets = [];
        switch (input)
        {
            case UiFocusChangedInput focus:
                packets = [Focus(focus.IsFocused, documentEpoch)];
                return true;
            case UiFocusLostInput:
            case UiKeyCancelInput:
            case UiPointerInput { Device: UiPointerDevice.Mouse, Phase: UiPointerPhase.Cancel }:
                packets = [Focus(false, documentEpoch)];
                return true;
            case UiPointerInput { Device: UiPointerDevice.Mouse } pointer:
                uint type = pointer.Phase switch
                {
                    UiPointerPhase.Move => 1,
                    UiPointerPhase.Down => 3,
                    UiPointerPhase.Up => 4,
                    _ => 0,
                };
                if (type == 0 || !Position(localPosition, scale, out int x, out int y))
                {
                    return false;
                }

                byte[] mouse = Packet(type, documentEpoch);
                Put(mouse, 8, x);
                Put(mouse, 12, y);
                packets = [mouse];
                return true;
            case UiWheelInput wheel:
                if (!Position(localPosition, scale, out int wx, out int wy)
                    || !Pixel(-wheel.Delta.X, scale, out int dx) || !Pixel(-wheel.Delta.Y, scale, out int dy))
                {
                    return false;
                }

                byte[] scroll = Packet(5, documentEpoch);
                Put(scroll, 8, wx);
                Put(scroll, 12, wy);
                Put(scroll, 16, dx);
                Put(scroll, 20, dy);
                packets = [scroll];
                return true;
            case UiKeyInput key:
                if (!MapKey(key.Key, out int vk, out int native))
                {
                    return false;
                }

                byte[] physical = Packet(key.IsDown ? 6u : 7u, documentEpoch);
                uint modifiers = Modifiers(key.Modifiers);
                if (key.IsDown && key.IsRepeat)
                {
                    modifiers |= 1u << 13;
                }

                if ((int)key.Key >= (int)Key.Numpad0 && (int)key.Key <= (int)Key.NumpadEnter)
                {
                    modifiers |= 1u << 9;
                }

                if (key.Key is Key.LeftShift or Key.LeftControl or Key.LeftAlt or Key.LeftMeta)
                {
                    modifiers |= 1u << 10;
                }
                else if (key.Key is Key.RightShift or Key.RightControl or Key.RightAlt or Key.RightMeta)
                {
                    modifiers |= 1u << 11;
                }

                U32(physical, 4, modifiers);
                Put(physical, 24, vk);
                Put(physical, 28, native);
                U32(physical, 36, ((key.Modifiers & UiModifiers.Alt) != 0 ? 1u : 0u) | (key.IsDown && key.IsRepeat ? 2u : 0u));
                packets = [physical];
                return true;
            case UiCommittedTextInput text:
                if (text.Text is null || text.Text.Length > 1024 || !ValidUnicode(text.Text))
                {
                    return false;
                }

                byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text.Text);
                if (utf8.Length > 3072)
                {
                    return false;
                }

                if (utf8.Length == 0)
                {
                    return true;
                }

                byte[] commit = new byte[BodySize + utf8.Length];
                Packet(11, documentEpoch).CopyTo(commit, 0);
                utf8.CopyTo(commit, BodySize);
                packets = [commit];
                return true;
            default:
                return false;
        }
    }

    internal static bool TryEncodeWheel(UiWheelInput wheel, Vector2? position, float scale, long epoch, ref Vector2 residual, out byte[][] packets)
    {
        packets = [];
        if (!Position(position, scale, out int x, out int y))
        {
            return false;
        }

        double dx = (-wheel.Delta.X * (double)scale) + residual.X;
        double dy = (-wheel.Delta.Y * (double)scale) + residual.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx > int.MaxValue || dx < int.MinValue || dy > int.MaxValue || dy < int.MinValue)
        {
            return false;
        }

        int wholeX = (int)Math.Truncate(dx);
        int wholeY = (int)Math.Truncate(dy);
        residual = new((float)(dx - wholeX), (float)(dy - wholeY));
        if (wholeX != 0 || wholeY != 0)
        {
            byte[] body = Packet(5, epoch);
            Put(body, 8, x);
            Put(body, 12, y);
            Put(body, 16, wholeX);
            Put(body, 20, wholeY);
            packets = [body];
        }

        return true;
    }

    internal static bool MapKey(Key key, out int vk, out int native)
    {
        (vk, native) = key switch
        {
            Key.A => (0x41, 38),
            Key.B => (0x42, 56),
            Key.C => (0x43, 54),
            Key.D => (0x44, 40),
            Key.E => (0x45, 26),
            Key.F => (0x46, 41),
            Key.G => (0x47, 42),
            Key.H => (0x48, 43),
            Key.I => (0x49, 31),
            Key.J => (0x4A, 44),
            Key.K => (0x4B, 45),
            Key.L => (0x4C, 46),
            Key.M => (0x4D, 58),
            Key.N => (0x4E, 57),
            Key.O => (0x4F, 32),
            Key.P => (0x50, 33),
            Key.Q => (0x51, 24),
            Key.R => (0x52, 27),
            Key.S => (0x53, 39),
            Key.T => (0x54, 28),
            Key.U => (0x55, 30),
            Key.V => (0x56, 55),
            Key.W => (0x57, 25),
            Key.X => (0x58, 53),
            Key.Y => (0x59, 29),
            Key.Z => (0x5A, 52),
            Key.Digit0 => (0x30, 19),
            Key.Digit1 or Key.Digit2 or Key.Digit3 or Key.Digit4 or Key.Digit5 or Key.Digit6 or Key.Digit7 or Key.Digit8 or Key.Digit9 => (0x31 + (int)key - (int)Key.Digit1, 10 + (int)key - (int)Key.Digit1),
            Key.Escape => (0x1B, 9),
            Key.Tab => (0x09, 23),
            Key.CapsLock => (0x14, 66),
            Key.Space => (0x20, 65),
            Key.Enter => (0x0D, 36),
            Key.Backspace => (0x08, 22),
            Key.LeftShift => (0x10, 50),
            Key.RightShift => (0x10, 62),
            Key.LeftControl => (0x11, 37),
            Key.RightControl => (0x11, 105),
            Key.LeftAlt => (0x12, 64),
            Key.RightAlt => (0x12, 108),
            Key.LeftMeta => (0x5B, 133),
            Key.RightMeta => (0x5C, 134),
            Key.ArrowUp => (0x26, 111),
            Key.ArrowDown => (0x28, 116),
            Key.ArrowLeft => (0x25, 113),
            Key.ArrowRight => (0x27, 114),
            Key.Insert => (0x2D, 118),
            Key.Delete => (0x2E, 119),
            Key.Home => (0x24, 110),
            Key.End => (0x23, 115),
            Key.PageUp => (0x21, 112),
            Key.PageDown => (0x22, 117),
            Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F10 => (0x70 + (int)key - (int)Key.F1, 67 + (int)key - (int)Key.F1),
            Key.F11 => (0x7A, 95),
            Key.F12 => (0x7B, 96),
            Key.F13 or Key.F14 or Key.F15 or Key.F16 or Key.F17 or Key.F18 or Key.F19 or Key.F20 or Key.F21 or Key.F22 or Key.F23 or Key.F24 => (0x7C + (int)key - (int)Key.F13, 191 + (int)key - (int)Key.F13),
            Key.Minus => (0xBD, 20),
            Key.Equal => (0xBB, 21),
            Key.LeftBracket => (0xDB, 34),
            Key.RightBracket => (0xDD, 35),
            Key.Backslash => (0xDC, 51),
            Key.Semicolon => (0xBA, 47),
            Key.Quote => (0xDE, 48),
            Key.Backquote => (0xC0, 49),
            Key.Comma => (0xBC, 59),
            Key.Period => (0xBE, 60),
            Key.Slash => (0xBF, 61),
            Key.NumLock => (0x90, 77),
            Key.PrintScreen => (0x2C, 107),
            Key.ScrollLock => (0x91, 78),
            Key.Pause => (0x13, 127),
            Key.Numpad0 => (0x60, 90),
            Key.Numpad1 => (0x61, 87),
            Key.Numpad2 => (0x62, 88),
            Key.Numpad3 => (0x63, 89),
            Key.Numpad4 => (0x64, 83),
            Key.Numpad5 => (0x65, 84),
            Key.Numpad6 => (0x66, 85),
            Key.Numpad7 => (0x67, 79),
            Key.Numpad8 => (0x68, 80),
            Key.Numpad9 => (0x69, 81),
            Key.NumpadMultiply => (0x6A, 63),
            Key.NumpadDivide => (0x6F, 106),
            Key.NumpadAdd => (0x6B, 86),
            Key.NumpadSubtract => (0x6D, 82),
            Key.NumpadDecimal => (0x6E, 91),
            Key.NumpadEnter => (0x0D, 104),
            Key.IntlBackslash => (0xE2, 94),
            Key.IntlRo => (0xE2, 97),
            Key.IntlYen => (0xDC, 132),
            _ => (0, 0),
        };

        return vk != 0;
    }

    private static byte[] Packet(uint type, long epoch)
    {
        byte[] packet = new byte[BodySize];
        U32(packet, 0, type);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(40), epoch);
        return packet;
    }

    private static bool Position(Vector2? position, float scale, out int x, out int y)
    {
        x = y = 0;
        return position is { } point && Pixel(point.X, scale, out x) && Pixel(point.Y, scale, out y);
    }

    private static bool Pixel(float value, float scale, out int pixel)
    {
        pixel = 0;
        double scaled = Math.Round((double)value * scale, MidpointRounding.AwayFromZero);
        if (!float.IsFinite(value) || !float.IsFinite(scale) || scale <= 0 || scaled < int.MinValue || scaled > int.MaxValue)
        {
            return false;
        }

        pixel = (int)scaled;
        return true;
    }

    private static uint Modifiers(UiModifiers modifiers) =>
        ((modifiers & UiModifiers.Shift) != 0 ? 1u << 1 : 0)
        | ((modifiers & UiModifiers.Control) != 0 ? 1u << 2 : 0)
        | ((modifiers & UiModifiers.Alt) != 0 ? 1u << 3 : 0)
        | ((modifiers & UiModifiers.Meta) != 0 ? 1u << 7 : 0);

    private static bool ValidUnicode(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i == text.Length || !char.IsLowSurrogate(text[i]))
                {
                    return false;
                }
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Put(byte[] packet, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(offset), value);

    private static void U32(byte[] packet, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset), value);
}
