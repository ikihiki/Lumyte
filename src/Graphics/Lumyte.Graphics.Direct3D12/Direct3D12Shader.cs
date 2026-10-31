using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Shader : IGraphicsShader
{
    private readonly byte[] _code;
    private bool _disposed;

    internal Direct3D12Shader(Direct3D12Device owner, ShaderArtifact artifact)
    {
        Owner = owner;
        Artifact = artifact;
        _code = artifact.GetTarget(ShaderTarget.Dxil).Code.ToArray();
        if (_code.Length < 32 || !_code.AsSpan(0, 4).SequenceEqual("DXBC"u8))
        {
            throw new ArgumentException("A DXIL container is required.", nameof(artifact));
        }
    }

    public ShaderArtifact Artifact { get; }

    internal Direct3D12Device Owner { get; }

    internal byte[] Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Owner.RequireReady();
            return _code;
        }
    }

    public void Dispose() => _disposed = true;
}
