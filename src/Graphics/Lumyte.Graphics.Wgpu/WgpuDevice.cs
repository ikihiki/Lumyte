using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

/// <summary>Native wgpu backend using the existing .NET binding, without a Lumyte C++ shim.</summary>
internal sealed unsafe class WgpuDevice : IDisposable
{
    internal const ulong CopyOffsetAlignmentInBytes = 4;
    internal const ulong CopySizeAlignmentInBytes = 4;
    private readonly A.Adapter _adapter;
    private readonly ConcurrentQueue<string> _errors;
    private readonly GCHandle _errorHandle;
    private bool _disposed;

    private WgpuDevice(A.Instance instance, A.Adapter adapter, A.Device native, ConcurrentQueue<string> errors, GCHandle errorHandle)
    {
        (Instance, _adapter, Native, _errors, _errorHandle) = (instance, adapter, native, errors, errorHandle);
    }

    public ulong MaxBufferSize
    {
        get
        {
            lock (Gate)
            {
                Check();
                return Native.GetLimits().maxBufferSize;
            }
        }
    }

    internal int ResourceCount { get; set; }

    internal int EncoderCount { get; set; }

    internal object Gate { get; } = new();

    internal A.Device Native { get; }

    internal A.Instance Instance { get; }

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
            A.Device native = adapter.RequestDeviceBlocking(new A.DeviceDescriptor { UncapturedErrorCallback = &OnError, UncapturedErrorUserdata = (void*)GCHandle.ToIntPtr(errorHandle), });
            return new(instance, adapter, native, errors, errorHandle);
        }
        catch
        {
            adapter?.Dispose();
            instance?.Dispose();
            errorHandle.Free();
            throw;
        }
    }

    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        lock (Gate)
        {
            Check();

            // Initial storage uses host wire bytes. Shader compatibility is checked separately.
            ulong elementSize = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            return new(elementSize, elementSize, CopyOffsetAlignmentInBytes, CopySizeAlignmentInBytes);
        }
    }

    public WgpuTypedBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            BufferLayout<T> layout = GetBufferLayout<T>();
            ulong sizeInBytes = layout.GetSizeInBytes(desc.Count);
            if (sizeInBytes == 0 || sizeInBytes > MaxBufferSize || sizeInBytes % CopySizeAlignmentInBytes != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(desc.Count), "Initial buffers require a positive, 4-byte aligned size within device limits.");
            }

            if (desc.Usage == 0 || (desc.Usage & ~(BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index)) != 0)
            {
                throw new ArgumentException("Unknown or empty buffer usage.");
            }

            if (!Enum.IsDefined(desc.Memory))
            {
                throw new ArgumentException("Unknown memory preference.");
            }

            var usage = (A.BufferUsage)0;
            if (desc.Usage.HasFlag(BufferUsage.CopySource))
            {
                usage |= A.BufferUsage.CopySrc;
            }

            if (desc.Usage.HasFlag(BufferUsage.CopyDestination))
            {
                usage |= A.BufferUsage.CopyDst;
            }

            if ((desc.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) != 0)
            {
                usage |= A.BufferUsage.Storage;
            }

            if (desc.Usage.HasFlag(BufferUsage.Index))
            {
                usage |= A.BufferUsage.Index;
            }

            if (desc.Memory == MemoryPreference.Readback)
            {
                if (desc.Usage != BufferUsage.CopyDestination)
                {
                    throw new ArgumentException("Readback buffers support only CopyDestination.");
                }

                usage |= A.BufferUsage.MapRead;
            }

            if (desc.Memory == MemoryPreference.Upload)
            {
                if (desc.Usage != BufferUsage.CopySource)
                {
                    throw new ArgumentException("Upload buffers support only CopySource.");
                }

                usage |= A.BufferUsage.MapWrite;
            }

            return new(this, Validated(Native.CreateBuffer(new A.BufferDescriptor { Size = sizeInBytes, Usage = usage })), desc, layout);
        }
    }

    public GpuReference<T> CreateReference<T>(BufferSlice slice)
        where T : unmanaged
    {
        lock (Gate)
        {
            Check();
            if (typeof(T) != typeof(uint))
            {
                throw new NotSupportedException("The initial data schema supports UInt32 only.");
            }

            if (slice.Buffer is null)
            {
                throw new ArgumentException("Invalid buffer slice.");
            }

            slice.Buffer.Check(this);
            if (slice.Offset % 4 != 0 || slice.Length % 4 != 0 || (slice.Buffer.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) == 0)
            {
                throw new ArgumentException("Reference must describe aligned shader data.");
            }

            return new(slice);
        }
    }

    public Texture CreateTexture(TextureDesc desc)
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            uint limit = Native.GetLimits().maxTextureDimension2D;
            if (desc.Width == 0 || desc.Height == 0 || desc.Width > limit || desc.Height > limit)
            {
                throw new ArgumentOutOfRangeException(nameof(desc));
            }

            if (desc.Usage == 0 || (desc.Usage & ~(TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled | TextureUsage.RenderAttachment)) != 0 || !Enum.IsDefined(desc.Format))
            {
                throw new ArgumentException("Invalid texture usage or format.");
            }

            var usage = (A.TextureUsage)0;
            if (desc.Usage.HasFlag(TextureUsage.CopySource))
            {
                usage |= A.TextureUsage.CopySrc;
            }

            if (desc.Usage.HasFlag(TextureUsage.CopyDestination))
            {
                usage |= A.TextureUsage.CopyDst;
            }

            if (desc.Usage.HasFlag(TextureUsage.Sampled))
            {
                usage |= A.TextureUsage.TextureBinding;
            }

            if (desc.Usage.HasFlag(TextureUsage.RenderAttachment))
            {
                usage |= A.TextureUsage.RenderAttachment;
            }

            return new(this, Validated(Native.CreateTexture(new A.TextureDescriptor { Size = new WGPUExtent3D { width = desc.Width, height = desc.Height, depthOrArrayLayers = 1 }, Format = desc.Format == TextureFormat.Rgba8Unorm ? WGPUTextureFormat.RGBA8Unorm : WGPUTextureFormat.RGBA8UnormSrgb, Dimension = WGPUTextureDimension._2D, Usage = usage, })), desc);
        }
    }

    /// <summary>Loads an offline shader embedded in the supplied DLL.</summary>
    public ShaderModule CreateShader(System.Reflection.Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ?? throw new ArgumentException($"Shader resource '{resourceName}' was not found in '{assembly.FullName}'.", nameof(resourceName));
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        using Stream? reflectionStream = assembly.GetManifestResourceStream(resourceName + ".reflection.json");
        using StreamReader? reflectionReader = reflectionStream is null ? null : new StreamReader(reflectionStream);
        return CreateShader(reader.ReadToEnd(), reflectionReader?.ReadToEnd());
    }

    /// <summary>Loads WGSL produced offline from Slang; does not compile Slang at runtime.</summary>
    public ShaderModule CreateShader(string wgsl, string? reflection = null)
    {
        lock (Gate)
        {
            Check();
            ArgumentException.ThrowIfNullOrWhiteSpace(wgsl);
            var schema = MaterialSchema.Parse(reflection);
            return new(this, Validated(Native.CreateShaderModule(new A.ShaderModuleDescriptor { Source = A.ShaderSource.FromWgsl(Encoding.UTF8.GetBytes(wgsl)), })), schema);
        }
    }

    public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            ArgumentNullException.ThrowIfNull(desc.Shader);
            desc.Shader.Check(this);
            ArgumentException.ThrowIfNullOrWhiteSpace(desc.EntryPoint);
            return new(this, Validated(Native.CreateComputePipeline(desc.Shader.Native, Encoding.UTF8.GetBytes(desc.EntryPoint))));
        }
    }

    public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            ArgumentNullException.ThrowIfNull(desc.Shader);
            desc.Shader.Check(this);
            ArgumentException.ThrowIfNullOrWhiteSpace(desc.VertexEntry);
            ArgumentException.ThrowIfNullOrWhiteSpace(desc.FragmentEntry);
            return new(this, Validated(Native.CreateRenderPipeline(desc.Shader.Native, Encoding.UTF8.GetBytes(desc.VertexEntry), desc.Shader.Native, Encoding.UTF8.GetBytes(desc.FragmentEntry), [new A.ColorTargetState(WGPUTextureFormat.RGBA8Unorm)])), desc.Shader.MaterialSchema);
        }
    }

    public Sampler CreateSampler(SamplerDesc desc)
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            if (!Enum.IsDefined(desc.MinFilter) || !Enum.IsDefined(desc.MagFilter) || !Enum.IsDefined(desc.AddressU) || !Enum.IsDefined(desc.AddressV))
            {
                throw new ArgumentException("Unknown sampler state.");
            }

            WGPUAddressMode Address(AddressMode mode) => mode switch
            {
                AddressMode.Repeat => WGPUAddressMode.Repeat,
                AddressMode.MirrorRepeat => WGPUAddressMode.MirrorRepeat,
                _ => WGPUAddressMode.ClampToEdge,
            };
            var native = new WGPUSamplerDescriptor
            {
                label = new()
                {
                    length = nuint.MaxValue,
                },
                addressModeU = Address(desc.AddressU),
                addressModeV = Address(desc.AddressV),
                addressModeW = WGPUAddressMode.ClampToEdge,
                minFilter = desc.MinFilter == FilterMode.Linear ? WGPUFilterMode.Linear : WGPUFilterMode.Nearest,
                magFilter = desc.MagFilter == FilterMode.Linear ? WGPUFilterMode.Linear : WGPUFilterMode.Nearest,
                mipmapFilter = WGPUMipmapFilterMode.Nearest,
                lodMinClamp = 0,
                lodMaxClamp = 32,
                maxAnisotropy = 1,
            };
            WGPUSamplerImpl* handle = WGPU.wgpuDeviceCreateSampler(Native.Handle, &native);
            try
            {
                CheckErrors();
            }
            catch
            {
                if (handle != null)
                {
                    WGPU.wgpuSamplerRelease(handle);
                }

                throw;
            }

            if (handle == null)
            {
                throw new InvalidOperationException("Sampler creation failed.");
            }

            return new(this, handle);
        }
    }

    public MaterialBindings CreateMaterialBindings<T>(MaterialBindingsDesc desc, ReadOnlySpan<T> materials, IShaderDataSerializer<T> serializer)
    {
        lock (Gate)
        {
            Check();
            ArgumentNullException.ThrowIfNull(desc);
            ArgumentNullException.ThrowIfNull(desc.Layout);
            WGPULimits limits = Native.GetLimits();
            if (desc.Layout.GetSizeInBytes((ulong)materials.Length) > limits.maxStorageBufferBindingSize || limits.maxSampledTexturesPerShaderStage < 4 || limits.maxSamplersPerShaderStage < 4)
            {
                throw new NotSupportedException("Material layout exceeds device binding limits.");
            }

            (byte[] Bytes, SampledPair[] Pairs) prepared = MaterialBindings.Prepare(this, desc, materials, serializer);
            Check();
            foreach (SampledPair pair in prepared.Pairs.Distinct())
            {
                pair.View.Check(this);
                pair.Sampler.Check(this);
            }

            return new(this, desc.Layout, (ulong)materials.Length, prepared.Bytes, prepared.Pairs);
        }
    }

    public CommandEncoder CreateCommandEncoder()
    {
        lock (Gate)
        {
            Check();
            A.CommandEncoder native = Native.CreateCommandEncoder();
            return new(this, native.Handle);
        }
    }

    public Submission Submit(CommandBuffer commands)
    {
        lock (Gate)
        {
            Check();
            commands.Check(this);
            commands.CheckUnsubmitted();
            WGPUCommandBufferImpl* handle = commands.Handle;
            WGPU.wgpuQueueSubmit(Native.Queue.Handle, 1, &handle);
            commands.MarkSubmitted();
            return new(this, Native.Queue.BeginOnSubmittedWorkDone(), commands.TakeResources(), commands.TakeMaterialTransfers());
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed)
            {
                return;
            }

            if (ResourceCount != 0 || EncoderCount != 0)
            {
                throw new InvalidOperationException("Dispose all resources/encoders and complete submissions before disposing the device.");
            }

            Native.Dispose();
            _adapter.Dispose();
            Instance.Dispose();
            _errorHandle.Free();
            _disposed = true;
        }
    }

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckErrors();
    }

    internal void CheckErrors()
    {
        if (_errors.TryPeek(out string? error))
        {
            throw new InvalidOperationException($"wgpu validation failed: {error}");
        }
    }

    internal T Validated<T>(T resource)
        where T : IDisposable
    {
        try
        {
            CheckErrors();
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(WGPUDeviceImpl** device, WGPUErrorType type, WGPUStringView message, void* user, void* unused)
    {
        // Never propagate a managed exception across the native callback boundary.
        try
        {
            var errors = (ConcurrentQueue<string>)GCHandle.FromIntPtr((nint)user).Target!;
            errors.Enqueue(message.data == null ? type.ToString() : $"{type}: {Marshal.PtrToStringUTF8((nint)message.data, checked((int)message.length))}");
        }
        catch
        {
        }
    }
}
