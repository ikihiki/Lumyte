using Lumyte.Graphics.Wgpu;
using System.Runtime.InteropServices;

using var device = WgpuDevice.Create();
using (var buffer = device.CreateBuffer(new BufferDesc {
    SizeInBytes = 32, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.CopySource | BufferUsage.CopyDestination,
}))
using (var readback = device.CreateBuffer(new BufferDesc {
    SizeInBytes = 32, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback,
}))
using (var shader = device.CreateShader(typeof(Program).Assembly, "Lumyte.Shaders.double.wgsl"))
using (var pipeline = device.CreateComputePipeline(new ComputePipelineDesc { Shader = shader }))
{
    device.WriteBuffer<uint>(buffer.Slice(0, 32), new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    using var arguments = pipeline.CreateArguments(device.CreateReference<uint>(buffer.Slice(0, 32)));
    using var encoder = device.CreateCommandEncoder();
    encoder.Dispatch(pipeline, arguments, 1);
    encoder.CopyBuffer(buffer.Slice(0, 32), readback.Slice(0, 32));
    using var commands = encoder.Finish();
    var submission = device.Submit(commands);
    await submission.WaitAsync();
    if (!device.ReadBuffer(readback).SequenceEqual(new uint[] { 2, 4, 6, 8, 10, 12, 14, 16 }))
        throw new InvalidOperationException("Compute readback mismatch.");
    Console.WriteLine("PASS: Slang → WGSL → wgpu compute, opaque data reference and buffer readback.");
}

using (var texture = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 }))
using (var view = texture.CreateView())
using (var readback = device.CreateBuffer(new BufferDesc {
    SizeInBytes = 64 * 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback,
}))
using (var shader = device.CreateShader(typeof(Program).Assembly, "Lumyte.Shaders.triangle.wgsl"))
using (var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }))
using (var encoder = device.CreateCommandEncoder())
{
    using (var render = encoder.BeginRenderPass(new RenderPassDesc { Target = view, ClearValue = new Color4(0, 0, 1, 1) }))
    {
        render.SetPipeline(pipeline);
        render.Draw(3);
    }
    encoder.CopyTextureToBuffer(texture, readback, 256);
    using var commands = encoder.Finish();
    await device.Submit(commands).WaitAsync();
    var pixels = device.ReadBuffer(readback);
    // Read exact bytes rather than assuming host UInt32 endianness.
    var center = MemoryMarshal.AsBytes(pixels.AsSpan()).Slice((32 * 64 + 32) * 4, 4);
    var corner = MemoryMarshal.AsBytes(pixels.AsSpan()).Slice(0, 4);
    if (!center.SequenceEqual(new byte[] { 255, 0, 0, 255 }) || !corner.SequenceEqual(new byte[] { 0, 0, 255, 255 }))
        throw new InvalidOperationException("Offscreen triangle readback mismatch.");
    Console.WriteLine("PASS: Slang → WGSL → RenderEncoder triangle, clear color and texture readback.");
}
