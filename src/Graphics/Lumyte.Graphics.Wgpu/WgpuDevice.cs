using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using A = Ahjo.Wgpu;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

/// <summary>Native wgpu backend using the existing .NET binding, without a Lumyte C++ shim.</summary>
public sealed unsafe class WgpuDevice : IDisposable
{
    internal object Gate { get; } = new();
    internal A.Device Native { get; }
    internal A.Instance Instance { get; }
    internal int ResourceCount;
    internal int EncoderCount;
    private readonly A.Adapter _adapter;
    private readonly ConcurrentQueue<string> _errors;
    private readonly GCHandle _errorHandle;
    private bool _disposed;
    public ulong MaxBufferSize { get { lock (Gate) { Check(); return Native.GetLimits().maxBufferSize; } } }

    private WgpuDevice(A.Instance instance, A.Adapter adapter, A.Device native, ConcurrentQueue<string> errors, GCHandle errorHandle)
        => (Instance, _adapter, Native, _errors, _errorHandle) = (instance, adapter, native, errors, errorHandle);

    public static WgpuDevice Create()
    {
        var errors = new ConcurrentQueue<string>();
        var errorHandle = GCHandle.Alloc(errors);
        A.Instance? instance = null;
        A.Adapter? adapter = null;
        try
        {
            instance = A.Instance.Create();
            adapter = instance.RequestAdapterBlocking();
            var native = adapter.RequestDeviceBlocking(new A.DeviceDescriptor {
                UncapturedErrorCallback = &OnError,
                UncapturedErrorUserdata = (void*)GCHandle.ToIntPtr(errorHandle),
            });
            return new(instance, adapter, native, errors, errorHandle);
        }
        catch { adapter?.Dispose(); instance?.Dispose(); errorHandle.Free(); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(WGPUDeviceImpl** device, WGPUErrorType type, WGPUStringView message, void* user, void* unused)
    {
        // Never propagate a managed exception across the native callback boundary.
        try
        {
            var errors = (ConcurrentQueue<string>)GCHandle.FromIntPtr((nint)user).Target!;
            errors.Enqueue(message.data == null ? type.ToString() :
                $"{type}: {Marshal.PtrToStringUTF8((nint)message.data, checked((int)message.length))}");
        }
        catch { }
    }

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckErrors();
    }
    internal void CheckErrors()
    {
        if (_errors.TryPeek(out var error)) throw new InvalidOperationException($"wgpu validation failed: {error}");
    }
    internal T Validated<T>(T resource) where T : IDisposable
    {
        try { CheckErrors(); return resource; }
        catch { resource.Dispose(); throw; }
    }

    public Buffer CreateBuffer(BufferDesc desc)
    {
        lock (Gate)
        {
            Check(); ArgumentNullException.ThrowIfNull(desc);
            if (desc.SizeInBytes == 0 || desc.SizeInBytes > MaxBufferSize || desc.SizeInBytes % 4 != 0)
                throw new ArgumentOutOfRangeException(nameof(desc.SizeInBytes), "Initial buffers require a positive, 4-byte aligned size within device limits.");
            if (desc.Usage == 0 || (desc.Usage & ~(BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index)) != 0)
                throw new ArgumentException("Unknown or empty buffer usage.");
            if (!Enum.IsDefined(desc.Memory)) throw new ArgumentException("Unknown memory preference.");
            var usage = (A.BufferUsage)0;
            if (desc.Usage.HasFlag(BufferUsage.CopySource)) usage |= A.BufferUsage.CopySrc;
            if (desc.Usage.HasFlag(BufferUsage.CopyDestination)) usage |= A.BufferUsage.CopyDst;
            if ((desc.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) != 0) usage |= A.BufferUsage.Storage;
            if (desc.Usage.HasFlag(BufferUsage.Index)) usage |= A.BufferUsage.Index;
            if (desc.Memory == MemoryPreference.Readback)
            {
                if (desc.Usage != BufferUsage.CopyDestination) throw new ArgumentException("Readback buffers support only CopyDestination.");
                usage |= A.BufferUsage.MapRead;
            }
            return new(this, Validated(Native.CreateBuffer(new A.BufferDescriptor { Size = desc.SizeInBytes, Usage = usage })), desc);
        }
    }

    public GpuReference<T> CreateReference<T>(BufferSlice slice) where T : unmanaged
    {
        lock (Gate)
        {
            Check();
            if (typeof(T) != typeof(uint)) throw new NotSupportedException("The initial data schema supports UInt32 only.");
            if (slice.Buffer is null) throw new ArgumentException("Invalid buffer slice.");
            slice.Buffer.Check(this);
            if (slice.Offset % 4 != 0 || slice.Length % 4 != 0 || (slice.Buffer.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) == 0)
                throw new ArgumentException("Reference must describe aligned shader data.");
            return new(slice);
        }
    }

    public void WriteBuffer<T>(BufferSlice destination, ReadOnlySpan<T> values) where T : unmanaged
    {
        lock (Gate)
        {
            Check(); if (destination.Buffer is null) throw new ArgumentException("Invalid slice.");
            destination.Buffer.Check(this); destination.Buffer.RequireIdle();
            ulong bytes = checked((ulong)values.Length * (ulong)sizeof(T));
            if (!destination.Buffer.Usage.HasFlag(BufferUsage.CopyDestination) || destination.Offset % 4 != 0 || bytes % 4 != 0 || bytes > destination.Length)
                throw new ArgumentException("Upload range or usage is invalid.");
            Native.Queue.WriteBuffer(destination.Buffer.Native, destination.Offset, values);
            CheckErrors();
        }
    }

    public uint[] ReadBuffer(Buffer buffer)
    {
        lock (Gate)
        {
            Check(); buffer.Check(this); buffer.RequireIdle();
            if (buffer.Memory != MemoryPreference.Readback || buffer.SizeInBytes > int.MaxValue)
                throw new ArgumentException("A completed readback buffer with a managed-size range is required.");
            var status = buffer.Native.MapBlocking(Instance, A.MapMode.Read, 0, (nuint)buffer.SizeInBytes);
            if (status != WGPUMapAsyncStatus.Success) throw new InvalidOperationException($"Buffer map failed: {status}");
            try { CheckErrors(); return buffer.Native.GetConstMappedRange<uint>(0, (nuint)buffer.SizeInBytes).ToArray(); }
            finally { buffer.Native.Unmap(); }
        }
    }

    public Texture CreateTexture(TextureDesc desc)
    {
        lock (Gate)
        {
            Check(); ArgumentNullException.ThrowIfNull(desc);
            var limit = Native.GetLimits().maxTextureDimension2D;
            if (desc.Width == 0 || desc.Height == 0 || desc.Width > limit || desc.Height > limit) throw new ArgumentOutOfRangeException(nameof(desc));
            return new(this, Validated(Native.CreateTexture(new A.TextureDescriptor {
                Size = new WGPUExtent3D { width = desc.Width, height = desc.Height, depthOrArrayLayers = 1 },
                Format = WGPUTextureFormat.RGBA8Unorm, Dimension = WGPUTextureDimension._2D,
                Usage = A.TextureUsage.RenderAttachment | A.TextureUsage.CopySrc,
            })), desc);
        }
    }

    /// <summary>Loads an offline shader embedded in the supplied DLL.</summary>
    public ShaderModule CreateShader(System.Reflection.Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Shader resource '{resourceName}' was not found in '{assembly.FullName}'.", nameof(resourceName));
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return CreateShader(reader.ReadToEnd());
    }

    /// <summary>Loads WGSL produced offline from Slang; does not compile Slang at runtime.</summary>
    public ShaderModule CreateShader(string wgsl)
    {
        lock (Gate)
        {
            Check(); ArgumentException.ThrowIfNullOrWhiteSpace(wgsl);
            return new(this, Validated(Native.CreateShaderModule(new A.ShaderModuleDescriptor {
                Source = A.ShaderSource.FromWgsl(Encoding.UTF8.GetBytes(wgsl)),
            })));
        }
    }
    public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        lock (Gate)
        {
            Check(); ArgumentNullException.ThrowIfNull(desc); ArgumentNullException.ThrowIfNull(desc.Shader); desc.Shader.Check(this);
            ArgumentException.ThrowIfNullOrWhiteSpace(desc.EntryPoint);
            return new(this, Validated(Native.CreateComputePipeline(desc.Shader.Native, Encoding.UTF8.GetBytes(desc.EntryPoint))));
        }
    }
    public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        lock (Gate)
        {
            Check(); ArgumentNullException.ThrowIfNull(desc); ArgumentNullException.ThrowIfNull(desc.Shader); desc.Shader.Check(this);
            ArgumentException.ThrowIfNullOrWhiteSpace(desc.VertexEntry); ArgumentException.ThrowIfNullOrWhiteSpace(desc.FragmentEntry);
            return new(this, Validated(Native.CreateRenderPipeline(desc.Shader.Native, Encoding.UTF8.GetBytes(desc.VertexEntry),
                desc.Shader.Native, Encoding.UTF8.GetBytes(desc.FragmentEntry),
                [new A.ColorTargetState(WGPUTextureFormat.RGBA8Unorm)])));
        }
    }
    public CommandEncoder CreateCommandEncoder()
    {
        lock (Gate)
        {
            Check(); var native = Native.CreateCommandEncoder();
            return new(this, native.Handle);
        }
    }
    public Submission Submit(CommandBuffer commands)
    {
        lock (Gate)
        {
            Check(); commands.Check(this); commands.CheckUnsubmitted();
            var handle = commands.Handle;
            WGPU.wgpuQueueSubmit(Native.Queue.Handle, 1, &handle);
            commands.MarkSubmitted();
            return new(this, Native.Queue.BeginOnSubmittedWorkDone(), commands.TakeResources());
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            if (ResourceCount != 0 || EncoderCount != 0) throw new InvalidOperationException("Dispose all resources/encoders and complete submissions before disposing the device.");
            Native.Dispose(); _adapter.Dispose(); Instance.Dispose(); _errorHandle.Free(); _disposed = true;
        }
    }
}
