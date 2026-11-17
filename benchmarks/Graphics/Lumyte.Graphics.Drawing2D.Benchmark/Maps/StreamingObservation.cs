namespace Lumyte.Graphics.Drawing2D.Benchmark.Maps;

/// <summary>Executed stage counts from a real A-B-A spatial demand cycle.</summary>
/// <param name="Builds">Actual MVT pipeline builds.</param>
/// <param name="Publications">Adopted visible generations.</param>
/// <param name="HeldRetirements">Retirements observed while release was blocked.</param>
/// <param name="Releases">Actual release stage completions before final disposal.</param>
/// <param name="ManagerReleases">Actual manager trim generation releases observed by M4.</param>
public readonly record struct StreamingObservation(int Builds, int Publications, int HeldRetirements, int Releases, ulong ManagerReleases);
