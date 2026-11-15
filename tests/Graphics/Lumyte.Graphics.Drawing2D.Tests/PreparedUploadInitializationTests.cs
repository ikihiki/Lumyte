using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Reference-bearing staging starts with a complete semantic write.</summary>
public sealed class PreparedUploadInitializationTests
{
    /// <summary>Every staging allocation is initialized once, including shared native-address storage.</summary>
    /// <param name="target">The backend storage route.</param>
    /// <param name="partitioned">Whether each draw needs its own admitted native table.</param>
    /// <returns>The preparation completion.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV, false)]
    [InlineData(ShaderTarget.Wgsl, false)]
    [InlineData(ShaderTarget.SpirV, true)]
    [InlineData(ShaderTarget.Wgsl, true)]
    public async Task InitialInstructionWritesCoverWholeAllocationAsync(ShaderTarget target, bool partitioned)
    {
        var device = new ProfilingDevice
        {
            Caps = new()
            {
                ShaderTarget = target,
                MaxBufferSize = 1UL << 30,
                ArgumentTableBindingLimits = new() { BuffersPerDraw = partitioned ? 2u : uint.MaxValue },
            },
        };
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var image = new DispatchTexture();
        using IGraphicsTextureView view = image.CreateView();
        using var targetImage = new DispatchTexture();
        using IGraphicsTextureView targetView = targetImage.CreateView();
        using var sampler = new DispatchSampler();
        var source = new Image2D(view, sampler);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 3; i++)
        {
            canvas.DrawImage(source, new(0, 0, 8, 8), new(i, 0, 8, 8));
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = targetView });
        Assert.Equal(target == ShaderTarget.SpirV && !partitioned ? 1 : 3, device.InstructionUploads.Count);
        Assert.Equal(partitioned || target == ShaderTarget.Wgsl ? 3 : 1, device.Tables.Count);
        Assert.All(device.InstructionUploads, upload =>
        {
            Assert.Equal(0ul, upload.Offset);
            Assert.Equal(upload.Count, (ulong)upload.Written);
        });

        int offset = 0;
        foreach ((ulong Count, ulong Offset, int Written) upload in device.InstructionUploads)
        {
            DrawingInstruction[] instructions = device.Instructions.Skip(offset).Take(upload.Written).ToArray();
            IArgumentTable table = ProfilingDevice.Owner(instructions[0].Texture);
            Assert.All(instructions, instruction =>
            {
                Assert.Same(table, ProfilingDevice.Owner(instruction.Texture));
                Assert.Same(table, ProfilingDevice.Owner(instruction.Sampler));
            });
            offset += upload.Written;
        }
    }
}
