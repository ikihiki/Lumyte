using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.DirectX12;

/// <summary>Owns a native Direct3D 12 device and its direct command queue.</summary>
public sealed unsafe partial class DirectX12Device :
    IGpuBackend,
    IDisposable
{
    private readonly D3D12 api;
    private ComPtr<ID3D12Device> device;
    private ComPtr<ID3D12CommandQueue> queue;
    private readonly Dictionary<ulong, MemoryRecord> memories = [];
    private readonly Dictionary<ulong, BufferRecord> buffers = [];
    private readonly Dictionary<ulong, BufferViewRecord> bufferViews = [];
    private readonly Dictionary<ulong, TextureRecord> textures = [];
    private readonly Dictionary<ulong, TextureViewRecord> textureViews = [];
    private readonly Dictionary<ulong, GpuSamplerDescription> samplers = [];
    private readonly Dictionary<ulong, PipelineRecord> pipelines = [];
    private readonly Dictionary<ulong, ComputePipelineRecord> computePipelines = [];
    private ulong nextHandle = 1;
    private bool disposed;

    private DirectX12Device(D3D12 api, ComPtr<ID3D12Device> device, ComPtr<ID3D12CommandQueue> queue)
    {
        this.api = api;
        this.device = device;
        this.queue = queue;
        MainQueue = new QueueAdapter(this);
    }

    public static DirectX12Device Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        }

        D3D12 api = D3D12.GetApi();
        ComPtr<ID3D12Device> device = default;
        ComPtr<ID3D12CommandQueue> queue = default;
        try
        {
            SilkMarshal.ThrowHResult(api.CreateDevice<IDXGIAdapter, ID3D12Device>(default, D3DFeatureLevel.Level110, out device));
            var description = new CommandQueueDesc(CommandListType.Direct, 0, CommandQueueFlags.None, 0);
            SilkMarshal.ThrowHResult(device.CreateCommandQueue<ID3D12CommandQueue>(&description, out queue));
            return new(api, device, queue);
        }
        catch
        {
            queue.Dispose();
            device.Dispose();
            api.Dispose();
            throw;
        }
    }

    public nint NativeDevice => (nint)device.Handle;
    public nint NativeQueue => (nint)queue.Handle;
    public IGpuQueue MainQueue { get; private set; } = null!;
    public GpuBackendCapabilities Capabilities =>
        GpuBackendCapabilities.ExplicitPlacement
        | GpuBackendCapabilities.MemoryAliasing
        | GpuBackendCapabilities.RasterPipeline
        | GpuBackendCapabilities.ComputePipeline;

    public byte[] RoundTripBuffer(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source.IsEmpty) { throw new ArgumentException("Source cannot be empty.", nameof(source)); }

        ulong size = checked((ulong)source.Length);
        ulong heapSize = (size + 65_535UL) & ~65_535UL;
        ComPtr<ID3D12Heap> uploadHeap = default;
        ComPtr<ID3D12Heap> defaultHeap = default;
        ComPtr<ID3D12Heap> readbackHeap = default;
        ComPtr<ID3D12Resource> upload = default;
        ComPtr<ID3D12Resource> gpu = default;
        ComPtr<ID3D12Resource> readback = default;
        ComPtr<ID3D12CommandAllocator> allocator = default;
        ComPtr<ID3D12GraphicsCommandList> commands = default;
        ComPtr<ID3D12Fence> fence = default;
        try
        {
            uploadHeap = CreateBufferHeap(HeapType.Upload, heapSize);
            defaultHeap = CreateBufferHeap(HeapType.Default, heapSize);
            readbackHeap = CreateBufferHeap(HeapType.Readback, heapSize);
            ResourceDesc description = new(
                ResourceDimension.Buffer, 0, size, 1, 1, 1, Format.FormatUnknown,
                new SampleDesc(1, 0), TextureLayout.LayoutRowMajor, ResourceFlags.None);
            SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(uploadHeap, 0, in description, ResourceStates.GenericRead, null, out upload));
            SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(defaultHeap, 0, in description, ResourceStates.CopyDest, null, out gpu));
            SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(readbackHeap, 0, in description, ResourceStates.CopyDest, null, out readback));

            void* mapped = null;
            SilkMarshal.ThrowHResult(upload.Map(0, (Silk.NET.Direct3D12.Range*)null, &mapped));
            fixed (byte* bytes = source) { System.Buffer.MemoryCopy(bytes, mapped, source.Length, source.Length); }
            upload.Unmap(0, (Silk.NET.Direct3D12.Range*)null);

            SilkMarshal.ThrowHResult(device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct, out allocator));
            SilkMarshal.ThrowHResult(device.CreateCommandList<ID3D12CommandAllocator, ID3D12PipelineState, ID3D12GraphicsCommandList>(0, CommandListType.Direct, allocator, default, out commands));
            commands.CopyBufferRegion(gpu, 0, upload, 0, size);
            ResourceTransitionBarrier transition = new(gpu, D3D12.ResourceBarrierAllSubresources, ResourceStates.CopyDest, ResourceStates.CopySource);
            ResourceBarrier barrier = new(ResourceBarrierType.Transition, ResourceBarrierFlags.None, null, transition);
            commands.ResourceBarrier(1, in barrier);
            commands.CopyBufferRegion(readback, 0, gpu, 0, size);
            SilkMarshal.ThrowHResult(commands.Close());
            ComPtr<ID3D12CommandList> executable = commands.QueryInterface<ID3D12CommandList>();
            try { queue.ExecuteCommandLists(1, ref executable); }
            finally { executable.Dispose(); }

            SilkMarshal.ThrowHResult(device.CreateFence<ID3D12Fence>(0, FenceFlags.None, out fence));
            SilkMarshal.ThrowHResult(queue.Signal(fence, 1));
            while (fence.GetCompletedValue() < 1) { Thread.Yield(); }

            mapped = null;
            Silk.NET.Direct3D12.Range readRange = new(0, checked((nuint)source.Length));
            SilkMarshal.ThrowHResult(readback.Map(0, &readRange, &mapped));
            byte[] result = new byte[source.Length];
            Marshal.Copy((nint)mapped, result, 0, result.Length);
            Silk.NET.Direct3D12.Range writtenRange = new(0, 0);
            readback.Unmap(0, &writtenRange);
            return result;
        }
        finally
        {
            fence.Dispose();
            commands.Dispose();
            allocator.Dispose();
            readback.Dispose();
            gpu.Dispose();
            upload.Dispose();
            readbackHeap.Dispose();
            defaultHeap.Dispose();
            uploadHeap.Dispose();
        }
    }

    private ComPtr<ID3D12Heap> CreateBufferHeap(HeapType type, ulong size)
        => CreateHeap(type, size, HeapFlags.AllowOnlyBuffers);

    private ComPtr<ID3D12Heap> CreateHeap(HeapType type, ulong size, HeapFlags flags, ulong alignment = 0)
    {
        HeapDesc description = new(size, new HeapProperties(type), alignment, flags);
        SilkMarshal.ThrowHResult(device.CreateHeap<ID3D12Heap>(in description, out ComPtr<ID3D12Heap> heap));
        return heap;
    }

    private static uint BytesPerPixel(GpuFormat format) => format switch
    {
        GpuFormat.R8Unorm => 1,
        GpuFormat.Rg8Unorm => 2,
        GpuFormat.Rgba8Unorm or GpuFormat.Bgra8Unorm
            or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8UnormSrgb
            or GpuFormat.R32Float or GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        foreach (ComputePipelineRecord pipeline in computePipelines.Values) { pipeline.Dispose(); }
        foreach (PipelineRecord pipeline in pipelines.Values) { pipeline.Dispose(); }
        foreach (TextureViewRecord view in textureViews.Values) { view.Dispose(); }
        foreach (TextureRecord texture in textures.Values) { texture.Dispose(); }
        foreach (BufferRecord buffer in buffers.Values) { buffer.Dispose(); }
        foreach (MemoryRecord memory in memories.Values) { memory.Dispose(); }
        queue.Dispose();
        device.Dispose();
        api.Dispose();
    }
}
