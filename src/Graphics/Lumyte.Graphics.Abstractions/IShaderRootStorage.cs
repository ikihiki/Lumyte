namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one command-local native root uniform allocation.</summary>
public interface IShaderRootStorage : IDisposable
{
    /// <summary>Gets the allocation capacity, including trailing alignment padding.</summary>
    int Size { get; }

    /// <summary>Synchronously copies bytes into an in-bounds range before sealing.</summary>
    /// <param name="bytes">The bytes to copy; the source is not retained.</param>
    /// <param name="offset">The destination byte offset.</param>
    void Write(ReadOnlySpan<byte> bytes, int offset);

    /// <summary>Completes host writes and makes the allocation ready for native binding.</summary>
    void Seal();
}
