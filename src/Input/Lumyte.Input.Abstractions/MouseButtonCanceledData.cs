namespace Lumyte.Input.Abstractions;

/// <summary>Cancels one mouse button without recognizing a normal release.</summary>
public sealed record MouseButtonCanceledData(MouseButton Button) : InputData;
