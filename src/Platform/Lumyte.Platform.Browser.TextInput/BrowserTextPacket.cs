namespace Lumyte.Platform.Browser.TextInput;

internal sealed record BrowserTextPacket(string Kind, string Session, string Generation, string Sequence, string Value, int Anchor, int Active, bool Composing, int Start, int Length, string? Code, bool Down, bool Repeat, bool Shift, bool Control, bool Alt, bool Meta);
