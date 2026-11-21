using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShader : IGraphicsShader, IGraphicsNativeOrigin
{
    private readonly VulkanDevice _owner;
    private readonly ShaderModule _native;
    private bool _disposed;

    internal VulkanShader(VulkanDevice owner, ShaderArtifact artifact)
    {
        (_owner, Artifact) = (owner, artifact);
        ShaderTargetData target = artifact.GetTarget(owner.Caps.ShaderTarget);
        if (target.Stage is ShaderStage.Mesh or ShaderStage.Task)
        {
            MeshShaderValidation.Validate(target, owner.Caps);
        }
        else if (target.Stage is not (ShaderStage.Vertex or ShaderStage.Fragment or ShaderStage.Compute))
        {
            throw new NotSupportedException("Unsupported Vulkan shader stage.");
        }

        VulkanPushConstants.ValidateStorage(owner, target);
        byte[] code = target.Code;
        fixed (byte* bytes = code)
        {
            var desc = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)bytes,
            };
            Result result = owner.Api.CreateShaderModule(owner.NativeDevice, &desc, null, out _native);
            if (result == Result.ErrorDeviceLost)
            {
                _owner.ReportLoss("Vulkan operation failed: device lost.");
            }

            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Vulkan shader module creation failed: {result}.");
            }
        }
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

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

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ObjectDisposedException.ThrowIf(Owner.Status == GraphicsDeviceStatus.Disposed, Owner);
        if (Owner.Status == GraphicsDeviceStatus.Lost)
        {
            throw new GraphicsDeviceLostException(Owner.Loss!);
        }

        _ = Native;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Api.DestroyShaderModule(_owner.NativeDevice, _native, null);
        _disposed = true;
    }
}
