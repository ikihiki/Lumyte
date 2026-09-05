namespace Lumyte.Input;

public readonly record struct TextChange(int Start, int OldLength, int NewLength);
