using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks outstanding frames cannot borrow the same pooled rectangle storage.</summary>
public sealed class RectanglePoolTests
{
    /// <summary>Checks bounded idle reuse and outstanding-frame isolation on wgpu.</summary>
    /// <returns>The asynchronous preparation checks.</returns>
    [GpuFact]
    public async Task WgpuReturnsOnlyCompletedUsesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAsync(device);
    }

    /// <summary>Checks bounded idle reuse and outstanding-frame isolation on Vulkan.</summary>
    /// <returns>The asynchronous preparation checks.</returns>
    [GpuFact]
    public async Task VulkanReturnsOnlyCompletedUsesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAsync(device);
    }

    private static async Task CheckAsync(IGraphicDevice native)
    {
        var device = new CountingDevice(native);
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture texture = native.CreateTexture(new() { Width = 32, Height = 32, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 1000; i++)
        {
            canvas.FillRect(new(0, 0, 2, 2), new Color2D(1, 0, 0, 1));
        }

        DrawList2D list = canvas.End();
        var outstanding = new List<PreparedDraw2D>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                outstanding.Add(await renderer.PrepareAsync(list, new() { View = view }));
            }

            // Four outstanding preparations need four independent upload/data pairs.
            Assert.Equal(8, device.Buffers);
            foreach (PreparedDraw2D prepared in outstanding)
            {
                prepared.Dispose();
            }

            outstanding.Clear();
            for (int i = 0; i < 4; i++)
            {
                outstanding.Add(await renderer.PrepareAsync(list, new() { View = view }));
            }

            // Only three idle entries are retained; the fourth is newly allocated.
            Assert.Equal(10, device.Buffers);
            renderer.Dispose();
        }
        finally
        {
            foreach (PreparedDraw2D prepared in outstanding)
            {
                prepared.Dispose();
            }
        }
    }

    private sealed class CountingDevice(IGraphicDevice inner) : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add => inner.DeviceLost += value;
            remove => inner.DeviceLost -= value;
        }

        public GraphicsDeviceStatus Status => inner.Status;

        public GraphicsDeviceLoss? Loss => inner.Loss;

        public DeviceCaps Caps => inner.Caps;

        public IGraphicsQueue Queue => inner.Queue;

        internal int Buffers { get; private set; }

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => inner.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => inner.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged
        {
            IGraphicsBuffer<T> buffer = inner.CreateBuffer(desc);
            Buffers++;
            return buffer;
        }

        public IGraphicsTexture CreateTexture(TextureDesc desc) => inner.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => inner.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => inner.CreateArgumentTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => inner.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => inner.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => inner.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => inner.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => inner.CreateGraphicsPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => inner.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderData => inner.CreateShaderDataBuffer<T>(artifact, count, memory);
    }
}
