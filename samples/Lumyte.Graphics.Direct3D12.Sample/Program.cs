using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Direct3D12;
using Lumyte.Graphics.Samples;

if (args.Length is < 1 or > 3 || args[0] is not ("hardware" or "warp") || args.Skip(1).Any(a => a is not ("suite" or "partial")))
{
    Console.Error.WriteLine("Usage: Lumyte.Graphics.Direct3D12.Sample hardware|warp [suite] [partial]");
    return 1;
}

using var owner = Direct3D12Device.Create(new() { UseWarp = args[0] == "warp", EnablePartialGraphicsPrograms = args.Contains("partial") });
IGraphicDevice device = owner;
if (args.Contains("suite"))
{
    Console.WriteLine(await BufferExercise.RunAsync(device));
    Console.WriteLine(TextureExercise.Run(device));
    Console.WriteLine(SamplerExercise.Run(device));
    Console.WriteLine(ArgumentTableExercise.Run(device));
    Console.WriteLine(ShaderExercise.Run(device));
    Console.WriteLine(await CommandExercise.RunAsync(device));
    Console.WriteLine(await PipelineExercise.RunAsync(device));
    Console.WriteLine(await ShaderBindingExercise.RunAsync(device));
    Console.WriteLine(await AdvancedCommandExercise.RunAsync(device));
    Console.WriteLine(await SemaphoreExercise.RunAsync(device));
    Console.WriteLine(await RenderGraphExercise.RunAsync(device));
    Console.WriteLine($"PASS: Agility SDK {owner.SdkVersion}, partial programs: {owner.PartialGraphicsProgramsEnabled}.");
    return 0;
}

using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = 4100, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
using IGraphicsBuffer<byte> gpu = device.CreateBuffer(new BufferDesc<byte> { Count = 4100, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
using IGraphicsBuffer<byte> readback = device.CreateBuffer(new BufferDesc<byte> { Count = 4100, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
byte[] expected = Enumerable.Range(0, 4093).Select(i => (byte)((i * 31) % 251)).ToArray();
await upload.MapAsync();
upload.SliceElements(3, 4093).CopyFrom(expected);
upload.Unmap();
using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
command.Barrier(new BufferBarrierDesc<byte> { Buffer = gpu.SliceElements(0, gpu.Count), Before = new(PipelineStage.None, ResourceAccess.None), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
command.CopyBuffer(upload.SliceElements(3, 4093), gpu.SliceElements(7, 4093));
command.Barrier(new BufferBarrierDesc<byte> { Buffer = gpu.SliceElements(0, gpu.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
command.CopyBuffer(gpu.SliceElements(7, 4093), readback.SliceElements(5, 4093));
command.Finish();
using IGraphicsSubmission submission = device.Queue.Submit([command]);
await submission.WaitAsync();
await readback.MapAsync();
byte[] actual = new byte[expected.Length];
readback.SliceElements(5, 4093).CopyTo(actual);
readback.Unmap();
if (!actual.SequenceEqual(expected) || command.State != CommandBufferState.Completed)
{
    throw new InvalidOperationException("Direct3D 12 readback verification failed.");
}

Console.WriteLine($"PASS: Direct3D 12 {args[0]}, Agility SDK {owner.SdkVersion}, Upload -> Default -> Readback, 4093 bytes with nonzero offsets; fence completed.");
return 0;
