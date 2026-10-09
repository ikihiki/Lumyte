using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserGraphicsPipeline
{
    private readonly Dictionary<string, JSObject> _variants = [];

    internal JSObject Resolve(RenderStateSnapshot state, TextureFormat[] formats)
    {
        ValidateAlive();
        PipelineValidation.Draw(Desc, state, formats, FragmentOutputs);
        string formatJson = "[" + string.Join(',', formats.Select(f => (int)f)) + "]";
        string key = state.Key + ":" + formatJson;
        if (_variants.TryGetValue(key, out JSObject? cached))
        {
            return cached;
        }

        JSObject pipeline = BrowserInterop.CreateGraphicsPipeline(_owner.Handle, _vertex.Native, VertexData.EntryPoint, _fragment!.Native, FragmentData!.EntryPoint, state.Key, formatJson);
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
