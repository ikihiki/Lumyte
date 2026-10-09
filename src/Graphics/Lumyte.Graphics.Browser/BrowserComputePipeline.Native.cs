using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserComputePipeline
{
    private JSObject? _native;

    internal JSObject Native
    {
        get
        {
            ValidateAlive();
            return _native!;
        }
    }

    private void Initialize() => _native = BrowserInterop.CreateComputePipeline(_owner.Handle, _compute.Native, Data.EntryPoint);

    private void DisposeNative() => _native!.Dispose();
}
