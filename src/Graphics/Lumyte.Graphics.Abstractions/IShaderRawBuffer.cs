namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides backend raw storage metadata.</summary>
public interface IShaderRawBuffer
{
    /// <summary>Gets the unmanaged C# element type used for resource admission.</summary>
    Type ElementType { get; }

    /// <summary>Gets its conventional shader element name.</summary>
    string ShaderTypeName { get; }

    /// <summary>Gets resource usage capabilities, independent of a registration's chosen storage view.</summary>
    BufferUsage Usage { get; }

    /// <summary>Gets the backing allocation size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the backend-owned native storage handle.</summary>
    object ShaderHandle { get; }
}
