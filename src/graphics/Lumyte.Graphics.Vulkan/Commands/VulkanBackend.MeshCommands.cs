using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private sealed partial class CommandRecord
    {
        public override void DispatchMesh(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
        {
            VerifyInsideRendering();
            VerifyMeshSupport();
            PushRoot(rootData);
            Owner.drawMeshTasks(Current, x, y, z);
        }

        public override void DispatchMeshIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
        {
            VerifyInsideRendering();
            VerifyMeshSupport();
            NativeDrawIndirectInfo info = DrawArguments(arguments, 12);
            PushRoot(rootData);
            Owner.drawMeshTasksIndirect(Current, &info);
        }

        private void VerifyMeshSupport()
        {
            if (!Owner.supportsMeshShaders) { throw new NotSupportedException("This Vulkan device does not support mesh shaders."); }
        }
    }
}
