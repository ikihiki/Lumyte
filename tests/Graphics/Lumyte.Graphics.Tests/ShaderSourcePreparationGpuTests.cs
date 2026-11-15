using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Reads helper-free numeric wire roots on the actual backends.</summary>
public sealed class ShaderSourcePreparationGpuTests
{
    /// <summary>Vulkan reads all four numeric components with diagnostics off and on.</summary>
    /// <returns>The completed readback.</returns>
    [GpuFact]
    public async Task VulkanNumericWireReadbackAsync() => await CheckAsync(ShaderTarget.SpirV);

    /// <summary>Wgpu reads float3 at C# offset four rather than uniform offset sixteen.</summary>
    /// <returns>The completed readback.</returns>
    [GpuFact]
    public async Task WgpuNumericWireReadbackAsync() => await CheckAsync(ShaderTarget.Wgsl);

    private static async Task CheckAsync(ShaderTarget target)
    {
        Assert.Equal(4, Marshal.OffsetOf<Numeric>(nameof(Numeric.Position)).ToInt32());
        foreach (string rootName in new[] { "arguments", "customPayload" })
        {
            string source = $$$"""
                struct Numeric { float Header; float3 Position; };
                ConstantBuffer<Numeric> {{{rootName}}};
                float4 main() : SV_Target0
                {
                    return float4({{{rootName}}}.Header + dot({{{rootName}}}.Position, float3(2,3,5)), {{{rootName}}}.Position) / 255.0;
                }
                """;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Stage = ShaderStage.Fragment, Target = target }, timeout.Token);
            ShaderArtifact vertex = await new SlangShaderCompiler().CompileAsync(new() { Source = "float4 main(uint i : SV_VertexID) : SV_Position { return float4(i == 0 ? float2(-1,-1) : (i == 1 ? float2(3,-1) : float2(-1,3)), 0, 1); }", Stage = ShaderStage.Vertex, Target = target }, timeout.Token);
            foreach (bool validation in new[] { false, true })
            {
                using VulkanDevice? vulkan = target == ShaderTarget.SpirV ? VulkanDevice.Create() : null;
                using WgpuDevice? wgpu = target == ShaderTarget.Wgsl ? WgpuDevice.Create() : null;
                IGraphicDevice raw = (IGraphicDevice?)vulkan ?? wgpu!;
                using ValidationDevice? layer = validation ? new(raw) : null;
                IGraphicDevice device = layer ?? raw;
                if (validation)
                {
                    ShaderTransfer.ReflectFields(typeof(Numeric));
                }

                using IGraphicsShader shader = device.CreateShader(artifact);
                using IGraphicsShader vertexShader = device.CreateShader(vertex);
                using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertexShader, FragmentShader = shader });
                using IGraphicsTexture result = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
                using IGraphicsTextureView view = result.CreateView();
                TextureCopyLayout layout = device.GetTextureCopyLayout(result.Format);
                uint pitch = checked(((layout.BytesPerTexel + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
                using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
                using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
                commands.Barrier(new TextureBarrierDesc { Texture = result, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
                IRenderEncoder encoder = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
                try
                {
                    encoder.SetPipeline(pipeline);
                    encoder.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
                    encoder.SetViewport(new(0, 0, 1, 1, 0, 1));
                    encoder.SetScissor(new(0, 0, 1, 1));
                    encoder.SetBlendConstant(new(0, 0, 0, 0));
                    encoder.SetStencilReference(0);
                    var value = new Numeric { Header = 7, Position = new(11, 13, 17) };
                    encoder.SetArguments(in value, rootParameter: rootName, shaderTypeName: "Numeric");
                    value.Header = 999;
                    value.Position = new(999);
                    encoder.Draw(3);
                }
                finally
                {
                    encoder.End();
                }

                commands.Barrier(new TextureBarrierDesc { Texture = result, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
                commands.CopyTextureToBuffer(new() { Texture = result, Width = 1, Height = 1 }, new() { Buffer = readback.SliceElements(0, pitch), BytesPerRow = pitch, RowsPerImage = 1 });
                commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, pitch), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
                commands.Finish();
                using IGraphicsSubmission submission = device.Queue.Submit([commands]);
                await submission.WaitAsync(timeout.Token);
                await readback.MapAsync(timeout.Token);
                byte[] actual = new byte[pitch];
                readback.CopyTo(actual);
                readback.Unmap();
                Assert.Equal(new byte[] { 153, 11, 13, 17 }, actual[..4]);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Numeric
    {
        public float Header;
        public Vector3 Position;
    }
}
