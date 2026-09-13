using System.Runtime.InteropServices.JavaScript;
using System.Text;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private static readonly UTF8Encoding shaderUtf8 = new(false, true);

    private sealed class ShaderModuleResource(WebGpuBackend owner, JSObject handle,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics) : P.GpuShaderModuleHandle
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly JSObject Handle = handle;
        internal readonly Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics = diagnostics;
        internal bool Destroyed;
    }

    public P.GpuShaderModuleHandle CreateShaderModule(string wgsl)
    {
        ArgumentNullException.ThrowIfNull(wgsl);
        RequireAvailable();
        shaderUtf8.GetByteCount(wgsl); // JSON must not silently replace an unpaired UTF-16 surrogate.
        var result = CreateObject(device, "shaderModule", new { code = wgsl }, []);
        return new ShaderModuleResource(this, result.Handle, result.Diagnostics);
    }

    public void DestroyShaderModule(P.GpuShaderModuleHandle module)
    {
        runtime.RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ShaderModuleResource resource = RequireShaderModule(module);
        resource.Destroyed = true;
        resource.Handle.Dispose();
    }

    private ShaderModuleResource RequireShaderModule(P.GpuShaderModuleHandle module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module is not ShaderModuleResource resource || !ReferenceEquals(resource.Owner, this))
        { throw new ArgumentException("Shader module belongs to another device.", nameof(module)); }
        ObjectDisposedException.ThrowIf(resource.Destroyed, module);
        return resource;
    }
}
