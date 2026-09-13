using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private struct RasterEncodingState
    {
        internal F.RenderPassEncoderHandle Pass;
        internal RasterPipelineResource? Selected;
        internal bool PipelineChanged;
    }

    private void PrepareRasterPipelines(CommandRecording recording, HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        RasterPipelineResource? selected = null;
        int rootLength = 0;
        foreach (RecordedCommand command in recording.Commands)
        {
            switch (command)
            {
                case BeginRenderingCommand: selected = null; rootLength = 0; break;
                case RasterPipelineCommand pipeline: selected = RequireRasterPipeline(pipeline.Pipeline); break;
                case RasterRootCommand root: rootLength = root.Bytes.Length; break;
                case DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand when selected != null:
                    if ((uint)rootLength != selected.ImmediateSize)
                    { throw new InvalidOperationException("Draw requires the complete root byte sequence specified by the program's ImmediateSize."); }
                    EnsureRasterPipeline(selected);
                    dependencies.Add(selected.Diagnostics);
                    break;
            }
        }
    }

    private unsafe bool TryEncodeRaster(CommandRecording recording, RecordedCommand command, F.CommandEncoderHandle encoder,
        ref RasterEncodingState state, HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        switch (command)
        {
            case BeginRenderingCommand begin:
                state.Pass = BeginRenderPass(recording, encoder, begin, dependencies);
                state.Selected = null;
                state.PipelineChanged = false;
                break;
            case EndRenderingCommand:
                F.WebGPU_FFI.RenderPassEncoderEnd(state.Pass);
                F.WebGPU_FFI.RenderPassEncoderRelease(state.Pass);
                state.Pass = default;
                break;
            case RasterPipelineCommand pipeline:
                state.Selected = RequireRasterPipeline(pipeline.Pipeline);
                state.PipelineChanged = true;
                break;
            case ViewportScissorCommand region:
                P.GpuViewport v = region.Viewport;
                P.GpuScissorRect s = region.Scissor;
                F.WebGPU_FFI.RenderPassEncoderSetViewport(state.Pass, v.X, v.Y, v.Width, v.Height, v.MinDepth, v.MaxDepth);
                F.WebGPU_FFI.RenderPassEncoderSetScissorRect(state.Pass, s.X, s.Y, s.Width, s.Height);
                break;
            case StencilReferenceCommand stencil:
                F.WebGPU_FFI.RenderPassEncoderSetStencilReference(state.Pass, stencil.Reference);
                break;
            case BlendConstantCommand blend:
                N.Color color = MapClearColor(blend.Color);
                F.WebGPU_FFI.RenderPassEncoderSetBlendConstant(state.Pass, &color);
                break;
            case RasterBindingsCommand bindings:
                BindingsResource binding = RequireBindings(bindings.Bindings);
                dependencies.Add(binding.Diagnostics);
                fixed (uint* offsets = bindings.DynamicOffsets)
                {
                    F.WebGPU_FFI.RenderPassEncoderSetBindGroup(state.Pass, bindings.Group, binding.Handle,
                        (nuint)bindings.DynamicOffsets.Length, offsets);
                }
                break;
            case RasterRootCommand root:
                fixed (byte* bytes = root.Bytes)
                { F.WebGPU_FFI.RenderPassEncoderSetImmediates(state.Pass, 0, bytes, (nuint)root.Bytes.Length); }
                break;
            case DrawCommand draw:
                ApplyRasterPipeline(ref state);
                F.WebGPU_FFI.RenderPassEncoderDraw(state.Pass, draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance);
                break;
            case DrawIndexedCommand draw:
                ApplyRasterPipeline(ref state);
                ApplyIndices(state.Pass, draw.Indices, draw.Format, dependencies);
                F.WebGPU_FFI.RenderPassEncoderDrawIndexed(state.Pass, draw.IndexCount, draw.InstanceCount, draw.FirstIndex, draw.BaseVertex, draw.FirstInstance);
                break;
            case DrawIndirectCommand draw:
                ApplyRasterPipeline(ref state);
                BufferResource arguments = RequireBuffer(draw.Arguments.Buffer);
                dependencies.Add(arguments.Diagnostics);
                F.WebGPU_FFI.RenderPassEncoderDrawIndirect(state.Pass, arguments.Handle, draw.Arguments.Offset);
                break;
            case DrawIndexedIndirectCommand draw:
                ApplyRasterPipeline(ref state);
                ApplyIndices(state.Pass, draw.Indices, draw.Format, dependencies);
                BufferResource indexedArguments = RequireBuffer(draw.Arguments.Buffer);
                dependencies.Add(indexedArguments.Diagnostics);
                F.WebGPU_FFI.RenderPassEncoderDrawIndexedIndirect(state.Pass, indexedArguments.Handle, draw.Arguments.Offset);
                break;
            default: return false;
        }
        return true;
    }

    private static void ApplyRasterPipeline(ref RasterEncodingState state)
    {
        if (state.PipelineChanged && state.Selected != null)
        {
            F.WebGPU_FFI.RenderPassEncoderSetPipeline(state.Pass, state.Selected.Handle);
            state.PipelineChanged = false;
        }
    }

    private void ApplyIndices(F.RenderPassEncoderHandle pass, P.GpuBufferRange range, P.GpuIndexFormat format,
        HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        BufferResource indices = RequireBuffer(range.Buffer);
        dependencies.Add(indices.Diagnostics);
        F.WebGPU_FFI.RenderPassEncoderSetIndexBuffer(pass, indices.Handle, MapIndexFormat(format), range.Offset, range.Length!.Value);
    }

    private static N.Color MapClearColor(P.GpuClearColor value)
        => new() { R = value.Red, G = value.Green, B = value.Blue, A = value.Alpha };

    private static N.IndexFormat MapIndexFormat(P.GpuIndexFormat format) => format switch
    {
        P.GpuIndexFormat.Uint16 => N.IndexFormat.Uint16,
        P.GpuIndexFormat.Uint32 => N.IndexFormat.Uint32,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
