using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserComputePipeline
{
    private readonly Dictionary<string, JSObject> _variants = [];
    private JSObject? _native;

    internal JSObject Native
    {
        get
        {
            ValidateAlive();
            return _native!;
        }
    }

    internal JSObject Resolve(ShaderBindingData? arguments)
    {
        if (arguments == null)
        {
            return Native;
        }

        ValidateAlive();
        if (!_variants.TryGetValue(arguments.Key, out JSObject? pipeline))
        {
            using JSObject shader = BrowserInterop.CreateShader(_owner.Handle, arguments.Specialize(Data));
            pipeline = BrowserInterop.CreateComputePipeline(_owner.Handle, shader, Data.EntryPoint, arguments.Key);
            _variants.Add(arguments.Key, pipeline);
        }

        return pipeline;
    }

    private void Initialize() => _native = BrowserInterop.CreateComputePipeline(_owner.Handle, _compute.Native, Data.EntryPoint);

    private void DisposeNative()
    {
        _native!.Dispose();
        foreach (JSObject pipeline in _variants.Values)
        {
            pipeline.Dispose();
        }

        _variants.Clear();
    }
}
