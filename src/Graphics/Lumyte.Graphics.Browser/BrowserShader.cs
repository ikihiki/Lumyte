using System.Runtime.InteropServices.JavaScript;
using System.Text;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserShader(BrowserDevice owner, ShaderArtifact artifact) : IGraphicsShader
{
    private readonly JSObject _handle = BrowserInterop.CreateShader(owner.Handle, Encoding.UTF8.GetString(artifact.GetTarget(owner.Caps.ShaderTarget).Code));
    private int _pipelineCount;
    private bool _disposed;

    public ShaderArtifact Artifact { get; } = artifact;

    internal BrowserDevice Owner => owner;

    internal JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    public void Dispose()
    {
        if (_pipelineCount != 0)
        {
            throw new InvalidOperationException("Dispose all programs before their shader module.");
        }

        if (_disposed)
        {
            return;
        }

        _handle.Dispose();
        _disposed = true;
        owner.ReleaseShader();
    }

    internal void RetainPipeline()
    {
        _ = Native;
        _pipelineCount++;
    }

    internal void ReleasePipeline() => _pipelineCount--;
}
