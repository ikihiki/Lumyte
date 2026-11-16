namespace Lumyte.Graphics.Tests;

/// <summary>Final allocation owned by an isolated test generation.</summary>
internal sealed class TestRootStorage(int size) : ITestShaderRootStorage
{
    public int Size => size;

    internal byte[] Bytes { get; } = new byte[size];

    public void Write(ReadOnlySpan<byte> bytes, int offset) => bytes.CopyTo(Bytes.AsSpan(offset));

    public void Seal()
    {
    }

    public void Dispose()
    {
    }
}
