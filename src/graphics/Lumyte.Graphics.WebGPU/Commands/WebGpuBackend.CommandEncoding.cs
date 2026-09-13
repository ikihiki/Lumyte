using P = Lumyte.Graphics.Portable;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private void PrepareComputePipelines(CommandRecording recording,
        HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        ComputePipelineResource? selected = null;
        int rootLength = 0;
        foreach (RecordedCommand command in recording.Commands)
        {
            switch (command)
            {
                case BeginComputeCommand: selected = null; rootLength = 0; break;
                case ComputePipelineCommand pipeline: selected = RequireComputePipeline(pipeline.Pipeline); break;
                case ComputeRootCommand root: rootLength = root.Bytes.Length; break;
                case DispatchCommand or DispatchIndirectCommand when selected != null:
                    // SetImmediates supports partial writes; this API promises a complete program root instead.
                    if ((uint)rootLength != selected.ImmediateSize)
                    { throw new InvalidOperationException("Dispatch requires the complete root byte sequence specified by the program's ImmediateSize."); }
                    EnsureComputePipeline(selected);
                    dependencies.Add(selected.Diagnostics);
                    break;
            }
        }
    }

    private unsafe F.CommandBufferHandle Encode(CommandRecording recording,
        HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        F.CommandEncoderHandle encoder = default;
        F.ComputePassEncoderHandle pass = default;
        F.CommandBufferHandle result = default;
        ComputePipelineResource? selected = null;
        bool pipelineChanged = false;
        try
        {
            PushScopes();
            try
            {
                var encoderDescription = new F.CommandEncoderDescriptorFFI();
                encoder = F.WebGPU_FFI.DeviceCreateCommandEncoder(device, &encoderDescription);
                RequireNativeObject((nuint)encoder, "command encoder");
                foreach (RecordedCommand command in recording.Commands)
                {
                    switch (command)
                    {
                        case BeginComputeCommand:
                            var passDescription = new F.ComputePassDescriptorFFI();
                            pass = F.WebGPU_FFI.CommandEncoderBeginComputePass(encoder, &passDescription);
                            RequireNativeObject((nuint)pass, "compute pass");
                            selected = null;
                            pipelineChanged = false;
                            break;
                        case EndComputeCommand:
                            F.WebGPU_FFI.ComputePassEncoderEnd(pass);
                            F.WebGPU_FFI.ComputePassEncoderRelease(pass);
                            pass = default;
                            break;
                        case ComputePipelineCommand pipeline:
                            selected = RequireComputePipeline(pipeline.Pipeline);
                            pipelineChanged = true;
                            break;
                        case ComputeBindingsCommand bindings:
                            BindingsResource binding = RequireBindings(bindings.Bindings);
                            dependencies.Add(binding.Diagnostics);
                            fixed (uint* offsets = bindings.DynamicOffsets)
                            {
                                F.WebGPU_FFI.ComputePassEncoderSetBindGroup(pass, bindings.Group, binding.Handle,
                                    (nuint)bindings.DynamicOffsets.Length, offsets);
                            }
                            break;
                        case ComputeRootCommand root:
                            fixed (byte* bytes = root.Bytes)
                            { F.WebGPU_FFI.ComputePassEncoderSetImmediates(pass, 0, bytes, (nuint)root.Bytes.Length); }
                            break;
                        case DispatchCommand dispatch:
                            if (pipelineChanged && selected != null)
                            { F.WebGPU_FFI.ComputePassEncoderSetPipeline(pass, selected.Handle); pipelineChanged = false; }
                            F.WebGPU_FFI.ComputePassEncoderDispatchWorkgroups(pass, dispatch.X, dispatch.Y, dispatch.Z);
                            break;
                        case DispatchIndirectCommand indirect:
                            if (pipelineChanged && selected != null)
                            { F.WebGPU_FFI.ComputePassEncoderSetPipeline(pass, selected.Handle); pipelineChanged = false; }
                            BufferResource arguments = RequireBuffer(indirect.Arguments.Buffer);
                            dependencies.Add(arguments.Diagnostics);
                            F.WebGPU_FFI.ComputePassEncoderDispatchWorkgroupsIndirect(pass, arguments.Handle, indirect.Arguments.Offset);
                            break;
                        case CopyBufferCommand copy:
                            BufferResource source = RequireBuffer(copy.Source.Buffer);
                            BufferResource destination = RequireBuffer(copy.Destination.Buffer);
                            dependencies.Add(source.Diagnostics);
                            dependencies.Add(destination.Diagnostics);
                            F.WebGPU_FFI.CommandEncoderCopyBufferToBuffer(encoder, source.Handle, copy.Source.Offset,
                                destination.Handle, copy.Destination.Offset, copy.Source.Length!.Value);
                            break;
                    }
                }
                var commandDescription = new F.CommandBufferDescriptorFFI();
                result = F.WebGPU_FFI.CommandEncoderFinish(encoder, &commandDescription);
                RequireNativeObject((nuint)result, "command buffer");
            }
            finally { dependencies.Add(PopScopes()); }
            return result;
        }
        catch
        {
            if ((nuint)result != 0) { F.WebGPU_FFI.CommandBufferRelease(result); }
            throw;
        }
        finally
        {
            if ((nuint)pass != 0) { F.WebGPU_FFI.ComputePassEncoderRelease(pass); }
            if ((nuint)encoder != 0) { F.WebGPU_FFI.CommandEncoderRelease(encoder); }
        }
    }

    private void RequireNativeObject(nuint handle, string kind)
    {
        if (handle != 0) { return; }
        status.Lose($"WebGPU returned no {kind} object.");
        status.ThrowIfFailed();
    }
}
