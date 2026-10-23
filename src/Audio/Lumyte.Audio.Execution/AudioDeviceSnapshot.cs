namespace Lumyte.Audio.Execution;

/// <summary>Captures audio availability and irreversible invalidation of prior Ready versions.</summary>
/// <param name="Version">The monotonically increasing service state version.</param>
/// <param name="Epoch">The most recently created physical device generation.</param>
/// <param name="Status">The service lifecycle state.</param>
/// <param name="InvalidatedThroughVersion">The largest irreversibly unusable Ready version.</param>
public readonly record struct AudioDeviceSnapshot(ulong Version, ulong Epoch, AudioDeviceServiceStatus Status, ulong InvalidatedThroughVersion);
