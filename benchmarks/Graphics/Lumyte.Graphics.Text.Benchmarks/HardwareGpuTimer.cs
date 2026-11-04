using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Silk.NET.Vulkan;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Lumyte.Graphics.Text.Benchmarks;

// Benchmark-only access to the active backend: no production API or IVT changes.
// One instance per frame slot. Caller must wait for submission completion before
// ReadMilliseconds, reuse, or Dispose, and must keep the device alive until Dispose.
internal sealed unsafe class HardwareGpuTimer : IDisposable
{
    private readonly VulkanDevice _owner;
    private readonly Vk _api;
    private readonly Device _device;
    private readonly double _timestampPeriod;
    private readonly uint _validBits;
    private QueryPool _pool;
    private IGraphicsCommandBuffer? _recording;
    private bool _ended;
    private bool _disposed;

    internal HardwareGpuTimer(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _owner = device;
        _api = ReadProperty<Vk>(device, "Api");
        _device = ReadProperty<Device>(device, "NativeDevice");
        PhysicalDevice physical = ReadProperty<PhysicalDevice>(device, "PhysicalDevice");
        uint queueFamily = ReadProperty<uint>(device, "QueueFamily");
        var driver = new PhysicalDeviceDriverProperties { SType = StructureType.PhysicalDeviceDriverProperties };
        var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &driver };
        _api.GetPhysicalDeviceProperties2(physical, &properties);
        PhysicalDeviceProperties identity = properties.Properties;
        string name = Marshal.PtrToStringUTF8((nint)identity.DeviceName) ?? "unknown";
        string driverName = Marshal.PtrToStringUTF8((nint)driver.DriverName) ?? "unknown";
        string driverInfo = Marshal.PtrToStringUTF8((nint)driver.DriverInfo) ?? "unknown";
        uint familyCount = 0;
        _api.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, null);
        var families = new QueueFamilyProperties[familyCount];
        fixed (QueueFamilyProperties* pointer = families)
        {
            _api.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, pointer);
        }

        if (queueFamily >= familyCount)
        {
            throw new InvalidOperationException("Active Vulkan queue family was not reported by its physical device.");
        }

        _validBits = families[queueFamily].TimestampValidBits;
        _timestampPeriod = identity.Limits.TimestampPeriod;
        Supported = _validBits is > 0 and <= 64 && double.IsFinite(_timestampPeriod) && _timestampPeriod > 0;
        DeviceDescription = FormattableString.Invariant($"{name}; vendor=0x{identity.VendorID:x4}; device=0x{identity.DeviceID:x4}; type={identity.DeviceType}; driver={driverName}; driverInfo={driverInfo}; driverVersion={identity.DriverVersion}; queueFamily={queueFamily}; timestampPeriodNs={_timestampPeriod}; timestampValidBits={_validBits}; gpuTimestamp={(Supported ? "supported" : "NoGpuTimestamp")}");
        if (!Supported)
        {
            return;
        }

        var create = new QueryPoolCreateInfo { SType = StructureType.QueryPoolCreateInfo, QueryType = QueryType.Timestamp, QueryCount = 2 };
        QueryPool pool;
        Check(_api.CreateQueryPool(_device, &create, null, &pool), "CreateQueryPool");
        _pool = pool;
    }

    internal string DeviceDescription { get; }

    internal bool Supported { get; }

    // Does not wait: owner must drain all submissions, including failure paths.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (Supported)
        {
            _api.DestroyQueryPool(_device, _pool, null);
        }

        _disposed = true;
    }

    // Call outside rendering, before the measured commands. Reset executes on GPU.
    // TOP_OF_PIPE -> BOTTOM_OF_PIPE spans GPU execution, not CPU queue latency;
    // adjacent submissions may overlap, so this is not an exclusive GPU busy counter.
    internal void Begin(IGraphicsCommandBuffer commands)
    {
        RequireSupported();
        if (_recording is not null)
        {
            throw new InvalidOperationException("Read the completed slot's timestamps before reusing its timer.");
        }

        VkCommandBuffer native = GetCommand(commands);
        _api.CmdResetQueryPool(native, _pool, 0, 2);
        _api.CmdWriteTimestamp(native, PipelineStageFlags.TopOfPipeBit, _pool, 0);
        _recording = commands;
        _ended = false;
    }

    // Call after measured work and outside rendering, before commands.Finish().
    // sameQueueSequence explicitly permits the last ordered command in a slot-owned
    // same-device queue sequence; caller awaits every submission before ReadMilliseconds.
    internal void End(IGraphicsCommandBuffer commands, bool sameQueueSequence = false)
    {
        RequireSupported();
        if (_recording is null || (!sameQueueSequence && !ReferenceEquals(_recording, commands)) || _ended)
        {
            throw new InvalidOperationException("Timestamp End must match exactly one Begin on the same command buffer.");
        }

        _api.CmdWriteTimestamp(GetCommand(commands), PipelineStageFlags.BottomOfPipeBit, _pool, 1);
        _ended = true;
    }

    // ONLY after caller has awaited the associated submission. No WAIT_BIT: a
    // premature/unavailable read throws instead of hiding a CPU wait or returning 0.
    internal double ReadMilliseconds()
    {
        RequireSupported();
        if (!_ended)
        {
            throw new InvalidOperationException("No ended timestamp pair is available.");
        }

        ulong* values = stackalloc ulong[2];
        Check(_api.GetQueryPoolResults(_device, _pool, 0, 2, (nuint)(2 * sizeof(ulong)), values, (ulong)sizeof(ulong), QueryResultFlags.Result64Bit), "GetQueryPoolResults (caller must await GPU completion)");
        ulong mask = _validBits == 64 ? ulong.MaxValue : (1UL << (int)_validBits) - 1;
        ulong ticks = unchecked(values[1] - values[0]) & mask;
        _recording = null;
        _ended = false;
        return ticks * _timestampPeriod / 1_000_000.0;
    }

    private static T ReadProperty<T>(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Benchmark Vulkan reflection contract changed: missing {name}.");
        return property.GetValue(instance) is T value
            ? value
            : throw new InvalidOperationException($"Benchmark Vulkan reflection contract changed: invalid {name}.");
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Hardware GPU timestamp {operation} failed: {result}.");
        }
    }

    private VkCommandBuffer GetCommand(IGraphicsCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.State != CommandBufferState.Recording || !ReferenceEquals(ReadProperty<VulkanDevice>(commands, "Owner"), _owner))
        {
            throw new InvalidOperationException("GPU timestamps require a recording Vulkan command buffer owned by this device.");
        }

        return ReadProperty<VkCommandBuffer>(commands, "Native");
    }

    private void RequireSupported()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Supported)
        {
            throw new NotSupportedException($"NoGpuTimestamp: {DeviceDescription}");
        }
    }
}
