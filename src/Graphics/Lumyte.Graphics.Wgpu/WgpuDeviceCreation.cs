using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

// Ahjo 0.7.0 does not expose deviceLostCallbackInfo. Keep this version-specific
// constructor access isolated until the upstream descriptor supports it.
internal sealed class WgpuDeviceCreation
{
    private readonly object _gate = new();
    private GraphicsDeviceLifetime? _lifetime;
    private GraphicsDeviceLoss? _loss;
    private GCHandle _callbackHandle;
    private nint _native;
    private WGPURequestDeviceStatus _status = WGPURequestDeviceStatus.Force32;

    internal unsafe A.Device Create(A.Instance instance, A.Adapter adapter)
    {
        _callbackHandle = GCHandle.Alloc(this);
        var requestHandle = GCHandle.Alloc(this);
        void* context = (void*)GCHandle.ToIntPtr(_callbackHandle);
        var empty = new WGPUStringView { length = nuint.MaxValue };
        var descriptor = new WGPUDeviceDescriptor
        {
            label = empty,
            defaultQueue = new WGPUQueueDescriptor { label = empty },
            deviceLostCallbackInfo = new()
            {
                mode = WGPUCallbackMode.AllowProcessEvents,
                callback = &OnLost,
                userdata1 = context,
            },
        };
        var callback = new WGPURequestDeviceCallbackInfo
        {
            mode = WGPUCallbackMode.AllowProcessEvents,
            callback = &OnCreated,
            userdata1 = (void*)GCHandle.ToIntPtr(requestHandle),
        };
        WGPU.wgpuAdapterRequestDevice(adapter.Handle, &descriptor, callback);
        while (_status == WGPURequestDeviceStatus.Force32)
        {
            instance.ProcessEvents();
            Thread.Yield();
        }

        requestHandle.Free();
        if (_status != WGPURequestDeviceStatus.Success || _native == 0)
        {
            // Failed creation has no device retaining the callback context.
            instance.ProcessEvents();
            Release();
            throw new InvalidOperationException($"WebGPU device creation failed: {_status}.");
        }

        return Wrap((WGPUDeviceImpl*)_native, instance);
    }

    internal void Attach(GraphicsDeviceLifetime lifetime)
    {
        GraphicsDeviceLoss? loss;
        lock (_gate)
        {
            _lifetime = lifetime;
            loss = _loss;
        }

        if (loss is not null)
        {
            lifetime.ReportLoss(loss.Reason, loss.Message);
        }
    }

    internal void Release()
    {
        if (_callbackHandle.IsAllocated)
        {
            _callbackHandle.Free();
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern unsafe A.Device Wrap(WGPUDeviceImpl* handle, A.Instance instance);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnCreated(WGPURequestDeviceStatus status, WGPUDeviceImpl* device, WGPUStringView message, void* context, void* unused)
    {
        var creation = (WgpuDeviceCreation)GCHandle.FromIntPtr((nint)context).Target!;
        creation._native = (nint)device;
        creation._status = status;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLost(WGPUDeviceImpl** device, WGPUDeviceLostReason reason, WGPUStringView message, void* context, void* unused)
    {
        // Never propagate managed exceptions through a native callback.
        try
        {
            var creation = (WgpuDeviceCreation)GCHandle.FromIntPtr((nint)context).Target!;
            int length = message.data == null ? 0 : checked((int)message.length);
            string text = length == 0 ? "WebGPU device lost." : System.Text.Encoding.UTF8.GetString((byte*)message.data, length);
            GraphicsDeviceLifetime? lifetime;
            lock (creation._gate)
            {
                creation._loss = new(reason.ToString(), text);
                lifetime = creation._lifetime;
            }

            lifetime?.ReportLoss(reason.ToString(), text);

            // wgpu-native can report DeviceLost again for subsequent native errors.
            // Retain callback userdata until the owner releases the native device.
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("WebGPU loss callback failed: {0}", error);
        }
    }
}
