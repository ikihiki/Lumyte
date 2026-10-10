namespace Lumyte.Input.Actions;
/// <summary>Defines input buffer options.</summary>
/// <param name = "Lifetime">The Lifetime value.</param>
/// <param name = "MaxEntries">The MaxEntries value.</param>
public sealed record InputBufferOptions(TimeSpan Lifetime, int MaxEntries);
