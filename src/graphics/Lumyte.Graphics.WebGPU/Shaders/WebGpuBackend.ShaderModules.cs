using System.Text;
using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private static readonly UTF8Encoding shaderUtf8 = new(false, true);

    private sealed class ShaderModuleResource(WebGpuBackend owner, F.ShaderModuleHandle handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuShaderModuleHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.ShaderModuleHandle Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public unsafe P.GpuShaderModuleHandle CreateShaderModule(string wgsl)
    {
        ArgumentNullException.ThrowIfNull(wgsl);
        byte[] code = shaderUtf8.GetBytes(wgsl);
        lock (gate)
        {
            RequireAvailable();
            F.ShaderModuleHandle handle = default;
            try
            {
                Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics;
                fixed (byte* pointer = code)
                {
                    var source = new F.ShaderSourceWGSLFFI
                    {
                        Chain = new() { SType = N.SType.ShaderSourceWGSL },
                        Code = new() { Data = pointer, Length = (nuint)code.Length },
                    };
                    var description = new F.ShaderModuleDescriptorFFI { NextInChain = &source.Chain };
                    PushScopes();
                    try { handle = F.WebGPU_FFI.DeviceCreateShaderModule(device, &description); }
                    finally { diagnostics = PopScopes(); }
                }
                if ((nuint)handle == 0)
                {
                    status.Lose("WebGPU shader module creation returned no object.");
                    status.ThrowIfFailed();
                }
                return new ShaderModuleResource(this, handle, diagnostics);
            }
            catch
            {
                if ((nuint)handle != 0) { F.WebGPU_FFI.ShaderModuleRelease(handle); }
                throw;
            }
        }
    }

    public void DestroyShaderModule(P.GpuShaderModuleHandle module)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ShaderModuleResource resource = RequireShaderModule(module);
            resource.Destroyed = true;
            F.WebGPU_FFI.ShaderModuleRelease(resource.Handle);
        }
    }

    private ShaderModuleResource RequireShaderModule(P.GpuShaderModuleHandle module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module is not ShaderModuleResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Shader module belongs to another device.", nameof(module)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, module);
        return resource;
    }

    internal Task<IReadOnlyList<P.GpuDiagnostic>> GetCreationDiagnostics(P.GpuShaderModuleHandle module)
    {
        lock (gate) { return RequireShaderModule(module).Diagnostics; }
    }
}
