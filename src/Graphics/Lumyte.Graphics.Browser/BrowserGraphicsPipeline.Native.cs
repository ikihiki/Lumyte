using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserGraphicsPipeline
{
    private readonly Dictionary<string, JSObject> _variants = [];

    internal JSObject Resolve(RenderStateSnapshot state, TextureFormat[] formats, ShaderBindingData? arguments = null)
    {
        ValidateAlive();
        PipelineValidation.Draw(Desc, state, formats, FragmentOutputs);
        string formatJson = "[" + string.Join(',', formats.Select(f => (int)f)) + "]";
        string key = state.Key + ":" + formatJson + ":" + arguments?.Key;
        if (_variants.TryGetValue(key, out JSObject? cached))
        {
            return cached;
        }

        using JSObject? vertex = arguments == null ? null : BrowserInterop.CreateShader(_owner.Handle, arguments.Specialize(VertexData));
        using JSObject? fragment = arguments == null ? null : BrowserInterop.CreateShader(_owner.Handle, arguments.Specialize(FragmentData!));
        JSObject pipeline = BrowserInterop.CreateGraphicsPipeline(_owner.Handle, vertex ?? _vertex.Native, VertexData.EntryPoint, fragment ?? _fragment!.Native, FragmentData!.EntryPoint, state.Key, formatJson, arguments?.Key ?? string.Empty);
        _variants.Add(key, pipeline);
        return pipeline;
    }

    private void Initialize()
    {
        // Actual attachment and draw state select the native variant at draw time.
    }

    private void DisposeNative()
    {
        foreach (JSObject native in _variants.Values)
        {
            native.Dispose();
        }

        _variants.Clear();
    }
}
