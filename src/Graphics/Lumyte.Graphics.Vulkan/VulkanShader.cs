using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShader : IGraphicsShader
{
    private readonly VulkanDevice _owner;
    private readonly ShaderModule _native;
    private bool _disposed;

    internal VulkanShader(VulkanDevice owner, ShaderArtifact artifact)
    {
        (_owner, Artifact) = (owner, artifact);
        byte[] code = artifact.GetCode();
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Api.DestroyShaderModule(_owner.NativeDevice, _native, null);
        _disposed = true;
        _owner.ReleaseShader();
    }
}
