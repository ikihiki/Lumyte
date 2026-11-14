using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserShaderBinding : IDisposable
{
    private readonly JSObject _native;

    internal BrowserShaderBinding(BrowserDevice owner, ShaderBindingData data, BrowserRootStorage rootStorage, bool compute = false)
    {
        if (owner.Caps.MaxUniformBuffersPerStage < 1)
        {
            throw new NotSupportedException("Root uniform emulation exceeds the enabled per-stage binding limits.");
        }

        rootStorage.Seal();
        _native = BrowserInterop.CreateShaderBinding(owner.Handle, data.Key, compute, rootStorage.Native, data.Map);
        try
        {
            foreach ((object resource, int index) in data.Textures)
            {
                BrowserInterop.AddShaderResource(_native, checked((int)data.Binding("texture", index)), 0, ((BrowserTextureView)resource).Native, 0, 0);
            }

            foreach ((object resource, int index) in data.Samplers)
            {
                BrowserInterop.AddShaderResource(_native, checked((int)data.Binding("sampler", index)), 1, ((BrowserSampler)resource).Native, 0, 0);
            }

            foreach ((string kind, IReadOnlyDictionary<ShaderBufferBinding, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
            {
                foreach ((ShaderBufferBinding range, int index) in resources)
                {
                    object handle = range.Resource is IShaderRawBuffer raw ? raw.ShaderHandle : ((IShaderDataSource)range.Resource).ShaderHandle;
                    BrowserInterop.AddShaderResource(_native, checked((int)data.Binding(kind, index)), 2, (JSObject)handle, range.OffsetInBytes, range.SizeInBytes);
                }
            }

            BrowserInterop.FinishShaderBinding(owner.Handle, _native);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal JSObject Native => _native;

    public void Dispose()
    {
        BrowserInterop.DestroyShaderBinding(_native);
        _native.Dispose();
    }
}
