namespace Lumyte.Input.Benchmark;

/// <summary>Immutable history workload observations.</summary>
/// <param name="Retained">Observed Retained.</param>
/// <param name="Gaps">Observed Gaps.</param>
/// <param name="Notifications">Observed Notifications.</param>
/// <param name="PressedScopes">Observed PressedScopes.</param>
/// <param name="FrozenCount">Observed FrozenCount.</param>
/// <param name="FrozenCountAfterUpdate">Observed FrozenCountAfterUpdate.</param>
/// <param name="Tail">Observed Tail.</param>
/// <param name="OriginScopes">Whether key repeats retain their original scope.</param>
/// <param name="OrderedCaptures">Whether captured sequences remain ordered.</param>
/// <param name="ReadCursor">Observed non-destructive cursor.</param>
public readonly record struct HistoryWave(int Retained, int Gaps, int Notifications, int PressedScopes, int FrozenCount, int FrozenCountAfterUpdate, ulong Tail, bool OriginScopes, bool OrderedCaptures, ulong ReadCursor);
