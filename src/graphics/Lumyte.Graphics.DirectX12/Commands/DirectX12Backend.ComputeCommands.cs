using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private sealed partial class NativeRecording
    {
        private NativeGpuComputePipelineHandle? computePipeline;

        public override void SetComputePipeline(NativeGpuComputePipelineHandle pipeline)
        {
            VerifyRecording();
            Owner.Owner.RequireComputePipeline(pipeline);
            computePipeline = pipeline;
        }

        public override void Dispatch(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
        {
            VerifyOutsideRendering();
            NativeGpuComputePipelineHandle pipeline = SelectedComputePipeline();
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                BindCompute(commands, pipeline, root);
                commands.Dispatch(x, y, z);
            });
        }

        public override void DispatchIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
        {
            VerifyOutsideRendering();
            NativeGpuComputePipelineHandle pipeline = SelectedComputePipeline();
            Owner.Owner.RequireLinear(arguments.Region);
            if (arguments.Size < 12)
            {
                throw new ArgumentException("The argument range must contain three 32-bit dispatch counts.", nameof(arguments));
            }
            byte[] root = CopyRootData(rootData);
            operations.Add(commands =>
            {
                LinearRecord linear = Owner.Owner.RequireLinear(arguments.Region);
                BindCompute(commands, pipeline, root);
                commands.ExecuteIndirect(Owner.Owner.dispatchSignature, 1, linear.Resource,
                    arguments.Offset, (ID3D12Resource*)null, 0);
            });
        }

        private NativeGpuComputePipelineHandle SelectedComputePipeline()
            => computePipeline ?? throw new InvalidOperationException("Select a compute pipeline before dispatching.");

        private static byte[] CopyRootData(ReadOnlySpan<byte> rootData)
        {
            if ((rootData.Length & 3) != 0)
            {
                throw new ArgumentException("Root data must contain a whole number of 32-bit constants.", nameof(rootData));
            }
            return rootData.ToArray();
        }

        private void BindCompute(ComPtr<ID3D12GraphicsCommandList8> commands,
            NativeGpuComputePipelineHandle pipeline, byte[] root)
        {
            ComputePipelineRecord record = Owner.Owner.RequireComputePipeline(pipeline);
            commands.SetPipelineState(record.Pipeline);
            // Heap selections must precede the directly-indexed root signature. Resolve
            // it at each dispatch so selection order cannot leave stale heap pointers.
            commands.SetComputeRootSignature(Owner.Owner.computeRootSignature);
            if (root.Length != 0)
            {
                fixed (byte* data = root)
                {
                    commands.SetComputeRoot32BitConstants(0, checked((uint)root.Length / 4), data, 0);
                }
            }
        }
    }
}
