using System.Diagnostics.CodeAnalysis;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides backend snapshot metadata.</summary>
public interface IShaderDataSource
{
    /// <summary>Gets the compiled element layout.</summary>
    IShaderDataLayout Layout { get; }

    /// <summary>Gets the logical backing size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the allocation memory purpose.</summary>
    MemoryPreference Memory { get; }

    /// <summary>Gets the backend native GPU buffer handle.</summary>
    object ShaderHandle { get; }

    /// <summary>Tries to get immutable, non-owning dependencies for the entire buffer.</summary>
    /// <param name="dependencies">The tracked buffer dependencies, or null when tracking is unavailable.</param>
    /// <returns>True when dependencies are known, including a known empty set; false when untracked.</returns>
    /// <exception cref="InvalidOperationException">Tracked dependencies are unknown after a failed transfer.</exception>
    bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies);

    /// <summary>Validates that the logical shader data source is alive.</summary>
    void ValidateAlive();
}
