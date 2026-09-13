using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private sealed partial class NativeRecording
    {
        public override void DispatchMesh(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
        {
            VerifyInsideRendering();
            RequireMeshSupport();
            NativeGpuRasterPipelineHandle pipeline = SelectedRasterPipeline();
            NativeGpuDepthStencilState state = depthStencil;
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                BindRaster(commands, pipeline, state, root);
                commands.DispatchMesh(x, y, z);
            });
        }

        public override void DispatchMeshIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
        {
            VerifyInsideRendering();
            RequireMeshSupport();
            NativeGpuRasterPipelineHandle pipeline = SelectedRasterPipeline();
            Owner.Owner.RequireLinear(arguments.Region);
            if (arguments.Size < 12)
            { throw new ArgumentException("The argument range does not contain one complete native mesh dispatch record.", nameof(arguments)); }
            NativeGpuDepthStencilState state = depthStencil;
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                LinearRecord argumentsBuffer = Owner.Owner.RequireLinear(arguments.Region);
                BindRaster(commands, pipeline, state, root);
                commands.ExecuteIndirect(Owner.Owner.meshDispatchSignature, 1, argumentsBuffer.Resource,
                    arguments.Offset, (ID3D12Resource*)null, 0);
            });
        }

        private void RequireMeshSupport()
        {
            if (!Owner.Owner.meshLimits.HasValue)
            { throw new NotSupportedException("This Direct3D 12 device does not support mesh dispatch."); }
        }
    }
}
