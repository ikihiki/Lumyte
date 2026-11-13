using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.UI.Application.Tests;

internal sealed class RetainedFrameFixture
{
    internal RetainedFrameFixture()
    {
        Prepared = new(new Device(), new() { View = new View() }, [], completed =>
        {
            Assert.Equal(CommandBufferState.Disposed, Commands.State);
            ReleaseAttempts++;
            if (ReleaseFailures-- > 0)
            {
                throw new ArithmeticException("retained cleanup");
            }

            Publications.Add(completed);
            Events.Add("retained");
        });
        Commands.OnDispose = () => Events.Add("commands");
        Frame = new(Guid.NewGuid()) { Retained = Prepared };
        Frame.Own(Commands);
    }

    internal UiHostPresenter.Frame Frame { get; }

    internal PreparedDrawingFrame2D Prepared { get; }

    internal TestCommands Commands { get; } = new();

    internal List<bool> Publications { get; } = [];

    internal List<string> Events { get; } = [];

    internal int ReleaseFailures { get; set; }

    internal int ReleaseAttempts { get; private set; }

    internal void Record()
    {
        Frame.RetainedRecorded = true;
        Prepared.Record(Commands);
    }

    internal void Submit(IGraphicsSubmission submission)
    {
        Frame.SubmissionAttempted = true;
        Commands.Submit();
        Frame.Submission = submission;
        Prepared.AttachSubmission(submission);
    }

    internal sealed class TestCommands : IGraphicsCommandBuffer
    {
        public CommandBufferState State { get; private set; }

        internal List<object> Events { get; } = [];

        internal Action? OnDispose { get; set; }

        internal int DisposeAttempts { get; private set; }

        internal int Failures { get; set; }

        public void Barrier(MemoryBarrierDesc barrier) => Events.Add(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => Events.Add(barrier);

        public void Barrier(TextureBarrierDesc barrier) => Events.Add(barrier);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : IShaderData => Events.Add(barrier);

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged => Events.Add("copy");

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : IShaderData => throw new NotSupportedException();

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => Events.Add("texture-copy");

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw new NotSupportedException();

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException();

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw new NotSupportedException();

        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => throw new NotSupportedException();

        public void Finish() => State = CommandBufferState.Executable;

        public void Dispose()
        {
            DisposeAttempts++;
            if (Failures-- > 0)
            {
                throw new ArithmeticException("command cleanup");
            }

            State = CommandBufferState.Disposed;
            OnDispose?.Invoke();
        }

        internal void Submit() => State = CommandBufferState.Submitted;
    }

    private sealed class View : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => throw new NotSupportedException();

        public TextureViewInfo Info => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class Device : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps { get; } = new() { MaxTextureDimension2D = 4096 };

        public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drawing preparation must not wait for device retirement.");

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => throw new NotSupportedException();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

        public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

        public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => new TestCommands();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : IShaderData => throw new NotSupportedException();
    }
}
