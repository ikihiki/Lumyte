using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShader : IGraphicsShader
{
    private readonly VulkanDevice _owner;
    private readonly ShaderModule _native;
    private int _pipelineCount;
    private bool _disposed;

    internal VulkanShader(VulkanDevice owner, ShaderArtifact artifact)
    {
        (_owner, Artifact) = (owner, artifact);
        byte[] code = artifact.GetTarget(owner.Caps.ShaderTarget).Code;
        fixed (byte* bytes = code)
        {
            var desc = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)bytes,
            };
            Result result = owner.Api.CreateShaderModule(owner.NativeDevice, &desc, null, out _native);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Vulkan shader module creation failed: {result}.");
            }
        }
    }

    public ShaderArtifact Artifact { get; }

    internal VulkanDevice Owner => _owner;

    internal ShaderModule Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _native;
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

        _owner.Api.DestroyShaderModule(_owner.NativeDevice, _native, null);
        _disposed = true;
        _owner.ReleaseShader();
    }

    internal void RetainPipeline()
    {
        _ = Native;
        _pipelineCount++;
    }

    internal void ReleasePipeline() => _pipelineCount--;
}
