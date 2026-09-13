using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12.Tests;

/// <summary>Retains native debug messages until the tested backend has finished its entire lifetime.</summary>
internal sealed unsafe class DirectX12ValidationScope : IDisposable
{
    private ComPtr<ID3D12InfoQueue> messages;
    private bool created;

    public INativeGpuBackend CreateBackend()
    {
        if (created) { throw new InvalidOperationException("A validation scope owns one backend's diagnostics."); }
        DirectX12Backend backend = DirectX12Backend.Create(new() { EnableValidation = true });
        try
        {
            // D3D12CreateDevice returns the existing device singleton for the default adapter,
            // which is also the adapter selected by DirectX12Backend. No private handles are exposed.
            using D3D12 api = D3D12.GetApi();
            using ComPtr<ID3D12Device> device = CreateDeviceReference(api);
            Marshal.ThrowExceptionForHR(device.QueryInterface(out messages));
            created = true;
            return backend;
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    public void AssertNoWarningsOrErrors()
    {
        Assert.True(created, "The test did not create a validation-enabled DirectX 12 backend.");
        var failures = new List<string>();
        ulong count = messages.GetNumStoredMessagesAllowedByRetrievalFilter();
        for (ulong index = 0; index < count; index++)
        {
            nuint size = 0;
            Marshal.ThrowExceptionForHR(messages.GetMessageA(index, (Message*)null, &size));
            Message* message = (Message*)NativeMemory.Alloc(size);
            try
            {
                Marshal.ThrowExceptionForHR(messages.GetMessageA(index, message, &size));
                if (message->Severity <= MessageSeverity.Warning)
                {
                    failures.Add($"{message->Severity} {message->ID}: {Marshal.PtrToStringAnsi((nint)message->PDescription)}");
                }
            }
            finally { NativeMemory.Free(message); }
        }
        Assert.Equal(0ul, messages.GetNumMessagesDiscardedByMessageCountLimit());
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public void Dispose() => messages.Dispose();

    private static ComPtr<ID3D12Device> CreateDeviceReference(D3D12 api)
    {
        Marshal.ThrowExceptionForHR(api.CreateDevice<IDXGIAdapter, ID3D12Device>(
            default, D3DFeatureLevel.Level110, out ComPtr<ID3D12Device> device));
        return device;
    }
}
