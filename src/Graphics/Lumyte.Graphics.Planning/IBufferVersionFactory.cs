namespace Lumyte.Graphics.Planning;

/// <summary>Clones a typed immutable buffer version without reflection.</summary>
internal interface IBufferVersionFactory
{
    /// <summary>Declares next.</summary>
    /// <param name = "version">The version.</param>
    /// <param name = "valid">The valid.</param>
    /// <returns>The declared result.</returns>
    PlanResource Next(int version, IReadOnlyList<PlanRange> valid);
}
