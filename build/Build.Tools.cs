using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Native backend: parley, parley-wasm, wgpu, usvg, direct3d12, or cef-supervisor.")]
    public string NativeBackend { get; set; } = "parley";

    [Parameter("Setup tool: wasm, parley, parley-wasm, dxc, lavapipe, or wasm-python.")]
    public string SetupTool { get; set; } = "wasm";

    [Parameter("Verification scope: environment, sdk, or native.")]
    public string VerifyScope { get; set; } = "environment";

    private Target Native => definition => definition.Executes(async () =>
    {
        switch (NativeBackend)
        {
            case "parley": await BuildAsync(wasm: false).ConfigureAwait(false); break;
            case "parley-wasm": await BuildAsync(wasm: true).ConfigureAwait(false); break;
            case "wgpu": await BuildWgpuNativeAsync().ConfigureAwait(false); break;
            case "usvg": await BuildUsvgNativeAsync().ConfigureAwait(false); break;
            case "cef-supervisor": await BuildCefSupervisorAsync().ConfigureAwait(false); break;
            case "direct3d12": await BuildDirect3D12NativeAsync().ConfigureAwait(false); break;
            default: throw new ArgumentException("Invalid --native-backend.");
        }
    });

    private Target Setup => definition => definition.Executes(async () =>
    {
        switch (SetupTool)
        {
            case "wasm": await InstallWasmToolsAsync().ConfigureAwait(false); break;
            case "parley": await SetupAsync(wasm: false).ConfigureAwait(false); break;
            case "parley-wasm": await SetupAsync(wasm: true).ConfigureAwait(false); break;
            case "dxc": await SetupDxcAsync().ConfigureAwait(false); break;
            case "lavapipe": ConfigureLavapipeManifest(); break;
            case "wasm-python": await PrepareWasmToolsAsync().ConfigureAwait(false); break;
            default: throw new ArgumentException("Invalid --setup-tool.");
        }
    });

    private Target Verify => definition => definition.Executes(async () =>
    {
        switch (VerifyScope)
        {
            case "sdk": await VerifySdkAsync().ConfigureAwait(false); break;
            case "native": await RunNativeSmokeAsync().ConfigureAwait(false); break;
            case "environment":
                await FormatNativeSourcesAsync(fix: false).ConfigureAwait(false);
                await CheckMarkdownAsync(fix: false).ConfigureAwait(false);
                await RunNativeSmokeAsync().ConfigureAwait(false);
                break;
            default: throw new ArgumentException("Invalid --verify-scope.");
        }
    });
}
