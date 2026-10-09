using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserShaderBinding : IDisposable
{
    private readonly JSObject _native;

    internal BrowserShaderBinding(BrowserDevice owner, ShaderBindingData data, bool compute = false)
    {
        _native = BrowserInterop.CreateShaderBinding(owner.Handle, data.Key, compute, data.Root, data.Map);
        try
        {
            foreach ((object resource, int index) in data.Textures)
            {
                BrowserInterop.AddShaderResource(_native, checked((int)data.Binding("texture", index)), 0, ((BrowserTextureView)resource).Native, 0);
            }

            foreach ((object resource, int index) in data.Samplers)
            {
                BrowserInterop.AddShaderResource(_native, checked((int)data.Binding("sampler", index)), 1, ((BrowserSampler)resource).Native, 0);
            }

            foreach ((string kind, IReadOnlyDictionary<object, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
            {
                foreach ((object resource, int index) in resources)
                {
                    if (resource is IShaderRawBuffer raw)
                    {
                        if (raw.SizeInBytes > owner.Caps.MaxStorageBufferBindingSize)
                        {
                            throw new NotSupportedException("Raw storage exceeds the device binding size.");
                        }

                        BrowserInterop.AddShaderResource(_native, checked((int)data.Binding(kind, index)), 2, (JSObject)raw.ShaderHandle, raw.SizeInBytes);
                    }
                    else
                    {
                        var source = (IShaderDataSource)resource;
                        BrowserInterop.AddShaderResource(_native, checked((int)data.Binding(kind, index)), 2, (JSObject)source.ShaderHandle, source.SizeInBytes);
                    }
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
