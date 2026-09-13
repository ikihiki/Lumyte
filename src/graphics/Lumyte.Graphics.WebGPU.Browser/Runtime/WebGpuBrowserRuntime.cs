using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Lumyte.Graphics.WebGPU.Browser;

/// <summary>A caller-owned JavaScript module used by Browser WebGPU devices on its creating thread.</summary>
/// <remarks>Keep this runtime alive until every backend borrowing it has been disposed.</remarks>
[SupportedOSPlatform("browser")]
public sealed class WebGpuBrowserRuntime : IDisposable
{
    internal const string ModuleName = "Lumyte.Graphics.WebGPU.Browser";
    [ThreadStatic] private static string? loadedModuleUrl;
    [ThreadStatic] private static Task<JSObject>? pendingImport;
    [ThreadStatic] private static bool moduleBound;
    private readonly JSObject module;
    private readonly int threadId;
    private bool disposed;

    private WebGpuBrowserRuntime(JSObject module)
    {
        this.module = module;
        threadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Imports the distributed lumyte-webgpu.js module from the supplied URL.</summary>
    public static async ValueTask<WebGpuBrowserRuntime> LoadAsync(string moduleUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleUrl);
        if (loadedModuleUrl is not null && !StringComparer.Ordinal.Equals(loadedModuleUrl, moduleUrl))
        { throw new InvalidOperationException("A Browser WebGPU interop module is already bound to another URL on this thread."); }
        Task<JSObject> import;
        if (pendingImport is not null) { import = pendingImport; }
        else
        {
            loadedModuleUrl = moduleUrl;
            try { import = pendingImport = ImportModuleAsync(moduleUrl); }
            catch
            {
                if (!moduleBound) { loadedModuleUrl = null; }
                throw;
            }
        }
        try
        {
            JSObject module = await import;
            moduleBound = true;
            if (ReferenceEquals(pendingImport, import)) { pendingImport = null; }
            return new(module);
        }
        catch
        {
            if (ReferenceEquals(pendingImport, import))
            {
                pendingImport = null;
                if (!moduleBound) { loadedModuleUrl = null; }
            }
            throw;
        }
    }

    private static async Task<JSObject> ImportModuleAsync(string moduleUrl)
    {
        if (!moduleBound)
        {
            // JSHost caches rejected imports by name. Validate the URL under its own stable name
            // before assigning the fixed name consumed by generated JS import bindings.
            JSObject candidate = await JSHost.ImportAsync($"{ModuleName}.candidate:{moduleUrl}", moduleUrl);
            candidate.Dispose();
        }
        return await JSHost.ImportAsync(ModuleName, moduleUrl);
    }

    internal void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
        { throw new InvalidOperationException("Browser WebGPU operations must run on the runtime's creating JavaScript thread."); }
    }

    internal void RequireAvailable()
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        RequireThread();
        if (disposed) { return; }
        disposed = true;
        module.Dispose();
    }
}
