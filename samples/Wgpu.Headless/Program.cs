using System.Runtime.InteropServices;
using Lumyte.Graphics;

using GraphicsDevice device = Graphics.CreateDevice();
using (IGraphicsBuffer<uint> buffer = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource | BufferUsage.CopyDestination, }))
using (IGraphicsBuffer<uint> readback = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback, }))
using (ShaderModule shader = device.CreateShader(typeof(Program).Assembly, "Lumyte.Shaders.double.wgsl"))
using (ComputePipeline pipeline = device.CreateComputePipeline(new ComputePipelineDesc { Shader = shader }))
{
    using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
    upload.CopyFrom(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    using ShaderArguments arguments = pipeline.CreateArguments(device.CreateReference<uint>(buffer.Slice(0, 8)));
    using CommandEncoder encoder = device.CreateCommandEncoder();
    encoder.RecordCopyBuffer(upload.Slice(0, 8), buffer.Slice(0, 8));
    encoder.Dispatch(pipeline, arguments, 1);
    encoder.RecordCopyBuffer(buffer.Slice(0, 8), readback.Slice(0, 8));
    using CommandBuffer commands = encoder.Finish();
    Submission submission = device.Submit(commands);
    await submission.WaitAsync();
    if (!ReadWords(readback).SequenceEqual(new uint[] { 2, 4, 6, 8, 10, 12, 14, 16 }))
    {
        throw new InvalidOperationException("Compute readback mismatch.");
    }

    Console.WriteLine("PASS: Slang → WGSL → wgpu compute, opaque data reference and buffer readback.");
}

using (IGraphicsTexture texture = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 }))
using (IGraphicsTextureView view = texture.CreateView())
using (IGraphicsBuffer<uint> readback = device.CreateBuffer(new BufferDesc<uint> { Count = 64 * 256 / sizeof(uint), Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback, }))
using (ShaderModule shader = device.CreateShader(typeof(Program).Assembly, "Lumyte.Shaders.triangle.wgsl"))
using (GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }))
using (CommandEncoder encoder = device.CreateCommandEncoder())
{
    using (RenderEncoder render = encoder.BeginRenderPass(new RenderPassDesc { Target = view, ClearValue = new Color4(0, 0, 1, 1) }))
    {
        render.SetPipeline(pipeline);
        render.Draw(3);
    }

    encoder.RecordCopyTextureToBuffer(texture, readback, 256);
    using CommandBuffer commands = encoder.Finish();
    await device.Submit(commands).WaitAsync();
    uint[] pixels = ReadWords(readback);

    // Read exact bytes rather than assuming host UInt32 endianness.
    Span<byte> center = MemoryMarshal.AsBytes(pixels.AsSpan()).Slice(((32 * 64) + 32) * 4, 4);
    Span<byte> corner = MemoryMarshal.AsBytes(pixels.AsSpan()).Slice(0, 4);
    if (!center.SequenceEqual(new byte[] { 255, 0, 0, 255 }) || !corner.SequenceEqual(new byte[] { 0, 0, 255, 255 }))
    {
        throw new InvalidOperationException("Offscreen triangle readback mismatch.");
    }

    Console.WriteLine("PASS: Slang → WGSL → RenderEncoder triangle, clear color and texture readback.");
}

// CPU result storage is allocated by this sample, not by the buffer backend.
static uint[] ReadWords(IGraphicsBuffer<uint> buffer)
{
    uint[] words = new uint[checked((int)buffer.Count)];
    buffer.CopyTo(words.AsSpan());
    return words;
}

byte[] materialPixels = Lumyte.Samples.MaterialDrawing.Run(device, typeof(Program).Assembly);
for (int y = 0; y < 4; y++)
{
    for (int x = 0; x < 8; x++)
    {
        byte[] expected = x < 4 ? [128, 0, 0, 255] : [0, 255, 0, 255];
        if (!materialPixels.AsSpan((y * 256) + (x * 4), 4).SequenceEqual(expected))
        {
            throw new InvalidOperationException($"GPU material texture selection mismatch at ({x}, {y}).");
        }
    }
}

Console.WriteLine("PASS: GPU material buffer selects red/green textures per pixel in one draw.");

byte[] squarePixels = Lumyte.Samples.TwentySquaresScene.Run(device, typeof(Program).Assembly);
Lumyte.Samples.TwentySquaresScene.Verify(squarePixels);
Console.WriteLine("PASS: 20 distinct textures and GPU materials render a 5×4 grid in 5 instanced draws; all 20,480 pixels match.");
if (args.Length == 2 && args[0] == "--scene-output")
{
    Lumyte.Samples.ScenePng.Write(args[1], squarePixels);
    Console.WriteLine($"GPU scene readback saved to {args[1]}.");
}
else if (args.Length != 0)
{
    throw new ArgumentException("Usage: Wgpu.Headless [--scene-output image.png]");
}
