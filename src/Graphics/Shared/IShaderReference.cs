using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics;

/// <summary>Provides backend-private snapshot metadata.</summary>
internal interface IShaderReference
{
    /// <summary>Gets snapshot resource metadata.</summary>
    object Table { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    object Resource { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    uint Slot { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    ulong OffsetInBytes { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    ulong Count { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    void Validate();
}

/// <summary>Provides backend-private snapshot metadata.</summary>
internal interface IShaderDataSource
{
    /// <summary>Gets snapshot resource metadata.</summary>
    ShaderDataLayout Layout { get; }

    /// <summary>Gets the logical backing size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets snapshot resource metadata.</summary>
    /// <param name="index">The logical element.</param>
    /// <returns>The immutable CPU value snapshot.</returns>
    ShaderValueSnapshot Read(ulong index);

    /// <summary>Gets snapshot resource metadata.</summary>
    void ValidateAlive();
}

/// <summary>Provides backend-private raw storage metadata.</summary>
internal interface IShaderRawBuffer
{
    /// <summary>Gets declared shader storage access.</summary>
    BufferUsage Usage { get; }

    /// <summary>Gets the backing allocation size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the backend-owned native storage handle.</summary>
    object ShaderHandle { get; }
}
