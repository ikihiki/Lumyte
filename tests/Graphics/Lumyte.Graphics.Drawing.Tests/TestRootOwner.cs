using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Fake backend owner pairing its host sink with production metadata.</summary>
internal sealed class TestRootOwner(ITestShaderRootStorage storage, ShaderRootArguments metadata) : IDisposable
{
    private bool _sealed;
    private bool _disposed;

    internal ITestShaderRootStorage Storage { get; } = storage;

    internal ShaderRootArguments Metadata { get; } = metadata;

    internal string RootParameter => Metadata.RootParameter;

    internal string ShaderTypeName => Metadata.ShaderTypeName;

    internal int WireSize => Metadata.WireSize;

    internal IShaderReferenceMetadata References => Metadata.References;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Storage.Dispose();
        }
    }

    internal void Admit(IReadOnlyCollection<IShaderReference> references)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Metadata.Admit(references);
    }

    internal void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sealed)
        {
            Storage.Seal();
            _sealed = true;
        }
    }
}
