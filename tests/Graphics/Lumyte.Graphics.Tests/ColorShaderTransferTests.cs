using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks merged color storage remains ordinary, unconverted C# wire data.</summary>
public sealed class ColorShaderTransferTests
{
    /// <summary>Encoded and linear wrappers need no generated codec, private-field accessors or color conversion.</summary>
    /// <param name="linear">Whether the input stores linear HDR components.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColorStorageCapturePreservesAuthoritativeBytes(bool linear)
    {
        if (linear)
        {
            CheckCapture(new LRgba<float>(-1, 2, 4, 0.5f), new(-1, 2, 4, 0.5f));
        }
        else
        {
            CheckCapture(new SRgba<float>(0.25f, 0.5f, 0.75f, 0.5f), new(0.25f, 0.5f, 0.75f, 0.5f));
        }
    }

    /// <summary>Both color encodings traverse valid native Vulkan copies with diagnostics disabled and enabled.</summary>
    /// <returns>The completed native copy checks.</returns>
    [GpuFact]
    public async Task VulkanColorStorageCopiesRemainUnconvertedAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = VulkanDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CopyColorsAsync(device);
        }
    }

    /// <summary>Both color encodings traverse valid native Wgpu copies with diagnostics disabled and enabled.</summary>
    /// <returns>The completed native copy checks.</returns>
    [GpuFact]
    public async Task WgpuColorStorageCopiesRemainUnconvertedAsync()
    {
        foreach (bool validation in new[] { false, true })
        {
            using var device = WgpuDevice.Create();
            if (validation)
            {
                device.EnableValidation();
            }

            await CopyColorsAsync(device);
        }
    }

    private static void CheckCapture<T>(T value, Vector4 components)
        where T : unmanaged
    {
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(T)));
        Assert.Equal(16, Unsafe.SizeOf<T>());
        byte[] expected = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray();
        ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in value, "color", "ColorWire");
        Assert.Equal(expected, snapshot.Data.ToArray());
        Assert.Equal(components, MemoryMarshal.Read<Vector4>(snapshot.Data.Span));
        Assert.Equal(expected, snapshot.Data.ToArray());
        Assert.Empty(snapshot.Values);
        Assert.Empty(snapshot.Fields);
        Assert.Null(snapshot.DiagnosticFields);
        Assert.Equal(typeof(T), snapshot.DiagnosticType);
    }

    private static async Task CopyColorsAsync(IGraphicDevice device)
    {
        await CopyAsync(device, new SRgba<float>[] { new(0.25f, 0.5f, 0.75f, 0.5f), new(1, 0, 0.5f, 1) });
        await CopyAsync(device, new LRgba<float>[] { new(-1, 2, 4, 0.5f), new(0.125f, 0.25f, 0.75f, 1) });
    }

    private static async Task CopyAsync<T>(IGraphicDevice device, T[] values)
        where T : unmanaged
    {
        using IGraphicsBuffer<T> upload = device.CreateBuffer<T>(new() { Count = 2, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<T> storage = device.CreateBuffer<T>(new() { Count = 2, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination });
        using IGraphicsBuffer<T> readback = device.CreateBuffer<T>(new() { Count = 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        Assert.Equal(16UL, storage.Layout.ElementStrideInBytes);
        Assert.Equal(32UL, storage.SizeInBytes);
        byte[] expected = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        await upload.MapAsync();
        upload.CopyFrom(values);
        upload.Unmap();
        values.AsSpan().Clear();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<T> { Buffer = upload.SliceElements(0, 2), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 2), storage.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<T> { Buffer = storage.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(storage.SliceElements(0, 2), readback.SliceElements(0, 2));
        commands.Barrier(new BufferBarrierDesc<T> { Buffer = readback.SliceElements(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        readback.CopyTo(values);
        readback.Unmap();
        Assert.Equal(expected, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
    }
}
