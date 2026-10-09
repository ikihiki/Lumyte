namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides backend snapshot metadata.</summary>
public interface IShaderDataSource
{
    /// <summary>Gets the compiled element layout.</summary>
    IShaderDataLayout Layout { get; }

    /// <summary>Gets the logical backing size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Reads an immutable CPU element snapshot.</summary>
    /// <param name="index">The logical element.</param>
    /// <returns>The immutable CPU value snapshot.</returns>
    ShaderValueSnapshot Read(ulong index);

    /// <summary>Validates that the logical shader data source is alive.</summary>
    void ValidateAlive();
}
