namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one command-local backend-defined opaque root payload.</summary>
public interface IShaderRootStorage : IDisposable
{
    /// <summary>Gets the nonnegative payload capacity in bytes; backend-specific padding may be included.</summary>
    int Size { get; }

    /// <summary>Synchronously copies bytes into an in-bounds range before sealing.</summary>
    /// <param name="bytes">The bytes to copy; the source is not retained.</param>
    /// <param name="offset">The destination byte offset.</param>
    void Write(ReadOnlySpan<byte> bytes, int offset);

    /// <summary>Commits and freezes the payload, preventing subsequent writes.</summary>
    void Seal();
}
