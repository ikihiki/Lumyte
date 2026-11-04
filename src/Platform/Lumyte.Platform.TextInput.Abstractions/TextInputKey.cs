using Lumyte.Input.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input key.</summary>
/// <param name = "Origin">The origin.</param>
/// <param name = "StateWindowId">The state window id.</param>
/// <param name = "Capture">The capture.</param>
/// <param name = "SessionId">The session id.</param>
/// <param name = "Generation">The generation.</param>
/// <param name = "Sequence">The sequence.</param>
/// <param name = "Key">The key.</param>
/// <param name = "IsDown">The is down.</param>
/// <param name = "IsRepeat">The is repeat.</param>
/// <param name = "Shift">The shift.</param>
/// <param name = "Control">The control.</param>
/// <param name = "Alt">The alt.</param>
/// <param name = "Meta">The meta.</param>
public sealed record TextInputKey(InputWindowId Origin, InputWindowId StateWindowId, InputCaptureStamp Capture, Guid SessionId, ulong Generation, ulong Sequence, Key Key, bool IsDown, bool IsRepeat, bool Shift, bool Control, bool Alt, bool Meta);
