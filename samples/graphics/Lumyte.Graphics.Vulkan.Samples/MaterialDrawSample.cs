using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics.Library;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Vulkan.Samples;

/// <summary>
/// Owns the shader-data buffers used by the AddDraw material sample. They are not bound as
/// vertex or index buffers; the vertex shader reads them through the bindless buffer array.
/// </summary>
internal sealed class MaterialDrawSample : IDisposable
{
    private readonly VulkanDevice device;
    private GpuMemoryAllocation vertexMemory;
    private GpuMemoryAllocation materialMemory;
    private GpuBufferHandle vertexBuffer;
    private GpuBufferHandle materialBuffer;
    private GpuBufferView vertexView;
    private GpuBufferView materialView;
    private bool disposed;

    private MaterialDrawSample(VulkanDevice device)
    {
        this.device = device;
    }

    internal DrawData Draw { get; private set; }

    internal static MaterialDrawSample Create(
        VulkanDevice device,
        GpuRasterPipelineHandle pipeline,
        GpuTextureHandle texture,
        GpuTextureDescription textureDescription,
        TextureId textureId,
        SamplerId sampler)
    {
        var result = new MaterialDrawSample(device);
        try
        {
            (result.vertexMemory, result.vertexBuffer, result.vertexView) = result.CreateBuffer([
                -0.76f, -0.70f, 0f, 0f,
                 0.76f, -0.70f, 1f, 0f,
                 0.76f,  0.70f, 1f, 1f,
                -0.76f, -0.70f, 0f, 0f,
                 0.76f,  0.70f, 1f, 1f,
                -0.76f,  0.70f, 0f, 1f,
            ]);
            (result.materialMemory, result.materialBuffer, result.materialView) = result.CreateBuffer([
                1.00f, 0.86f, 0.72f, 1.00f,
                2.00f, 2.00f, 0.00f, 0.00f,
            ]);

            var resources = new GpuResourceTable(1, 1, 2);
            resources.SetTexture(0, textureId);
            resources.SetSampler(0, sampler);
            resources.SetBuffer(0, result.vertexView.Id);
            resources.SetBuffer(1, result.materialView.Id);
            var material = new DrawMaterial(
                pipeline,
                resources,
                [new(texture, textureDescription)],
                [
                    new(
                        0,
                        result.vertexBuffer,
                        new(result.vertexBuffer.Size, GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource),
                        GpuStage.VertexShader),
                    new(
                        1,
                        result.materialBuffer,
                        new(result.materialBuffer.Size, GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource),
                        GpuStage.PixelShader),
                ]);
            result.Draw = new(
                material,
                new(6),
                new(Matrix4x4.Identity, Matrix4x4.Identity));
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        DestroyView(ref materialView);
        DestroyView(ref vertexView);
        DestroyBuffer(ref materialBuffer, ref materialMemory);
        DestroyBuffer(ref vertexBuffer, ref vertexMemory);
    }

    private (GpuMemoryAllocation Memory, GpuBufferHandle Buffer, GpuBufferView View) CreateBuffer(
        ReadOnlySpan<float> values)
    {
        ulong byteCount = checked((ulong)values.Length * sizeof(float));
        var description = new GpuBufferDescription(
            byteCount,
            GpuBufferUsage.ShaderData | GpuBufferUsage.CopySource);
        GpuBufferMemoryRequirements requirements = device.GetBufferMemoryRequirements(description);
        GpuMemoryAllocation memory = device.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle buffer = default;
        try
        {
            buffer = device.CreatePlacedBuffer(description, memory);
            MemoryMarshal.AsBytes(values).CopyTo(memory.MappedBytes());
            GpuBufferView view = device.CreateBufferView(buffer, default);
            return (memory, buffer, view);
        }
        catch
        {
            if (!buffer.IsNull) { device.DestroyBuffer(buffer); }
            device.FreeMemory(memory);
            throw;
        }
    }

    private void DestroyView(ref GpuBufferView view)
    {
        if (view.Id.IsNull) { return; }
        device.DestroyBufferView(view);
        view = default;
    }

    private void DestroyBuffer(
        ref GpuBufferHandle buffer,
        ref GpuMemoryAllocation memory)
    {
        if (!buffer.IsNull)
        {
            device.DestroyBuffer(buffer);
            buffer = default;
        }
        if (!memory.MemoryAddress.IsNull)
        {
            device.FreeMemory(memory);
            memory = default;
        }
    }
}
