namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides backend raw storage metadata.</summary>
public interface IShaderRawBuffer
{
    /// <summary>Gets declared shader storage access.</summary>
    BufferUsage Usage { get; }

    /// <summary>Gets the backing allocation size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the backend-owned native storage handle.</summary>
    object ShaderHandle { get; }
}
