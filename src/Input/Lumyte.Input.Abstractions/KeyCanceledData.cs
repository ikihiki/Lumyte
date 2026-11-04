namespace Lumyte.Input.Abstractions;

/// <summary>Cancels a logical key series without completing its activation.</summary>
/// <param name = "Key">The canceled physical key identity.</param>
public sealed record KeyCanceledData(Key Key) : InputData;
