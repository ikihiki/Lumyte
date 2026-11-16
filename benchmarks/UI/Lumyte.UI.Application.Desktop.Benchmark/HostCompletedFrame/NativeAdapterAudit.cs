using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ahjo.Wgpu.Native;

namespace Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

/// <summary>Uses the shipped public native log callback to audit this host's actual RequestAdapter result, not enumeration.</summary>
public sealed class NativeAdapterAudit : IDisposable
{
    private static NativeAdapterAudit? _current;
    private readonly object _gate = new();
    private readonly List<string> _selected = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="NativeAdapterAudit"/> class for one exclusive native startup.</summary>
    public unsafe NativeAdapterAudit()
    {
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
        {
            throw new InvalidOperationException("Native adapter audit requires exclusive host startup in this process.");
        }

        try
        {
            WGPU.wgpuSetLogCallback(&OnLog, null);
            WGPU.wgpuSetLogLevel(WGPULogLevel.Debug);
        }
        catch
        {
            Interlocked.Exchange(ref _current, null);
            throw;
        }
    }

    /// <summary>Recognizes only actual selected-adapter log lines with an explicit physical GPU type.</summary>
    /// <param name="line">The native log line, not a user-supplied adapter label.</param>
    /// <returns>Whether this line identifies a physical selected GPU rather than a fallback or enumeration entry.</returns>
    public static bool IsHardwareSelection(string line) =>
        line.Contains("Request adapter result AdapterInfo", StringComparison.Ordinal)
        && (line.Contains("device_type: IntegratedGpu", StringComparison.Ordinal) || line.Contains("device_type: DiscreteGpu", StringComparison.Ordinal))
        && !new[] { "llvmpipe", "lavapipe", "swiftshader", "software" }.Any(value => line.Contains(value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rejects missing, ambiguous and CPU/software actual adapter selections.</summary>
    /// <returns>The exact selected AdapterInfo native line for provenance.</returns>
    public string VerifyHardware()
    {
        lock (_gate)
        {
            if (_selected.Count != 1)
            {
                throw new InvalidOperationException($"Expected exactly one actual Request adapter result; observed {_selected.Count}. Hardware identity is unverified.");
            }

            string selected = _selected[0];
            if (!IsHardwareSelection(selected))
            {
                throw new InvalidOperationException($"Actual host selected a non-hardware or unidentified adapter: {selected}");
            }

            return selected;
        }
    }

    /// <inheritdoc/>
    public unsafe void Dispose()
    {
        if (!_disposed)
        {
            WGPU.wgpuSetLogLevel(WGPULogLevel.Warn);
            WGPU.wgpuSetLogCallback(null, null);
            Interlocked.Exchange(ref _current, null);
            _disposed = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnLog(WGPULogLevel level, WGPUStringView message, void* context)
    {
        try
        {
            NativeAdapterAudit? audit = Volatile.Read(ref _current);
            if (audit is null || message.data == null || message.length > 65536)
            {
                return;
            }

            string text = Encoding.UTF8.GetString((byte*)message.data, checked((int)message.length));

            // wgpu-core 29.0.1 instance.rs request_adapter logs this AFTER preference sorting,
            // for the one adapter it returns. '* AdapterInfo' enumeration lines are not accepted.
            if (text.Contains("Request adapter result AdapterInfo", StringComparison.Ordinal))
            {
                lock (audit._gate)
                {
                    audit._selected.Add(text);
                }
            }
        }
        catch
        {
            // Never propagate managed exceptions across an unmanaged callback.
        }
    }
}
