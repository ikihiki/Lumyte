namespace Lumyte.Input.Processing.Benchmark;

/// <summary>Immutable processing workload observations.</summary>
/// <param name="Records">Observed Records.</param>
/// <param name="Value">Observed Value.</param>
/// <param name="Swipes">Observed Swipes.</param>
/// <param name="Contacts">Observed Contacts.</param>
/// <param name="Captures">Observed Captures.</param>
public readonly record struct ProcessingWave(int Records, float Value, int Swipes, int Contacts, ulong[] Captures);
