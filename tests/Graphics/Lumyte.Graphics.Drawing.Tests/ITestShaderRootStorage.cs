namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Host sink used only by isolated writer and fake-backend tests.</summary>
internal interface ITestShaderRootStorage : IDisposable
{
    /// <summary>Gets the host sink capacity.</summary>
    int Size { get; }

    /// <summary>Writes bytes into the isolated sink.</summary>
    /// <param name="bytes">The source bytes.</param>
    /// <param name="offset">The destination byte offset.</param>
    void Write(ReadOnlySpan<byte> bytes, int offset);

    /// <summary>Ends writes to the fake backend allocation.</summary>
    void Seal();
}
