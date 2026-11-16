using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu.Tests;

/// <summary>Checks public native registry ownership, both submit entry points and reference counts.</summary>
internal static unsafe class NativeOwnershipProbe
{
    internal static void Run()
    {
        WGPUInstanceImpl* instance = WGPU.wgpuCreateInstance(null);
        WGPUAdapterImpl* adapter = null;
        WGPUDeviceImpl* device = null;
        WGPUQueueImpl* queue = null;
        try
        {
            nint result = 0;
            var adapterCallback = new WGPURequestAdapterCallbackInfo
            {
                mode = WGPUCallbackMode.AllowProcessEvents,
                callback = &OnAdapter,
                userdata1 = &result,
            };
            WGPURequestAdapterOptions options = default;
            WGPU.wgpuInstanceRequestAdapter(instance, &options, adapterCallback);
            while (result == 0)
            {
                WGPU.wgpuInstanceProcessEvents(instance);
            }

            if (result == -1)
            {
                throw new InvalidOperationException("Native adapter request failed.");
            }

            adapter = (WGPUAdapterImpl*)result;
            result = 0;
            var deviceCallback = new WGPURequestDeviceCallbackInfo
            {
                mode = WGPUCallbackMode.AllowProcessEvents,
                callback = &OnDevice,
                userdata1 = &result,
            };
            WGPUDeviceDescriptor descriptor = default;
            WGPU.wgpuAdapterRequestDevice(adapter, &descriptor, deviceCallback);
            while (result == 0)
            {
                WGPU.wgpuInstanceProcessEvents(instance);
            }

            if (result == -1)
            {
                throw new InvalidOperationException("Native device request failed.");
            }

            device = (WGPUDeviceImpl*)result;
            queue = WGPU.wgpuDeviceGetQueue(device);
            nuint baseline = Count(instance);
            for (int iteration = 0; iteration < 3000; iteration++)
            {
                WGPUCommandEncoderImpl* encoder = WGPU.wgpuDeviceCreateCommandEncoder(device, null);
                WGPUCommandBufferImpl* command = WGPU.wgpuCommandEncoderFinish(encoder, null);
                WGPU.wgpuCommandEncoderRelease(encoder);
                RequireCount(instance, baseline + 1);
                WGPU.wgpuCommandBufferAddRef(command);
                WGPU.wgpuCommandBufferRelease(command);
                RequireCount(instance, baseline + 1);
                WGPU.wgpuCommandBufferRelease(command);
                RequireCount(instance, baseline);
            }

            for (int iteration = 0; iteration < 100; iteration++)
            {
                WGPUCommandEncoderImpl* encoder = WGPU.wgpuDeviceCreateCommandEncoder(device, null);
                WGPUCommandBufferImpl* command = WGPU.wgpuCommandEncoderFinish(encoder, null);
                WGPU.wgpuCommandEncoderRelease(encoder);
                if (iteration % 2 == 0)
                {
                    WGPU.wgpuQueueSubmit(queue, 1, &command);
                }
                else
                {
                    WGPU.wgpuQueueSubmitForIndex(queue, 1, &command);
                }

                WGPU.wgpuDevicePoll(device, 1, null);
                WGPU.wgpuCommandBufferRelease(command);
                RequireCount(instance, baseline);
            }

            Console.WriteLine($"PASS native registry: 3000 discarded + AddRef/Release pairs; 50 Submit + 50 SubmitForIndex; commandBuffers={baseline} after every final Release.");
        }
        finally
        {
            if (queue != null)
            {
                WGPU.wgpuQueueRelease(queue);
            }

            if (device != null)
            {
                WGPU.wgpuDeviceRelease(device);
            }

            if (adapter != null)
            {
                WGPU.wgpuAdapterRelease(adapter);
            }

            WGPU.wgpuInstanceRelease(instance);
        }
    }

    private static nuint Count(WGPUInstanceImpl* instance)
    {
        WGPUGlobalReport report = default;
        WGPU.wgpuGenerateReport(instance, &report);
        return report.hub.commandBuffers.numKeptFromUser;
    }

    private static void RequireCount(WGPUInstanceImpl* instance, nuint expected)
    {
        nuint actual = Count(instance);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Native command registry ownership: expected={expected}, actual={actual}.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAdapter(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapter, WGPUStringView message, void* context, void* unused)
        => *(nint*)context = status == WGPURequestAdapterStatus.Success ? (nint)adapter : -1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDevice(WGPURequestDeviceStatus status, WGPUDeviceImpl* device, WGPUStringView message, void* context, void* unused)
        => *(nint*)context = status == WGPURequestDeviceStatus.Success ? (nint)device : -1;
}
