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

    /// <summary>Reads an immutable CPU element snapshot.</summary>
    /// <param name="index">The logical element.</param>
    /// <returns>The immutable CPU value snapshot.</returns>
    ShaderValueSnapshot Read(ulong index);

    /// <summary>Publishes metadata after an explicit GPU copy is submitted.</summary>
    /// <param name="index">The destination element.</param>
    /// <param name="value">The copied value metadata.</param>
    void SetTransferredValue(ulong index, ShaderValueSnapshot value);

    /// <summary>Validates that the logical shader data source is alive.</summary>
    void ValidateAlive();
}
