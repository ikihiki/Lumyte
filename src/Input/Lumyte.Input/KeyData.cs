namespace Lumyte.Input;

/// <summary>Records a physical key press, release or repeat.</summary>
/// <param name="Key">The Key value.</param>
/// <param name="IsDown">The IsDown value.</param>
/// <param name="IsRepeat">The IsRepeat value.</param>
public sealed record KeyData(Key Key, bool IsDown, bool IsRepeat) : InputData;
