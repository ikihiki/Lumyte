using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

[SupportedOSPlatform("browser")]
public static partial class BrowserCases
{
    public static void Main() { }

    [JSExport]
    public static string RuntimeSmoke() => OperatingSystem.IsBrowser() ? "browser-wasm" : "wrong-runtime";

    [JSExport]
    public static async Task<string> Run(string scenario)
    {
        object result = scenario switch
        {
            "Device" => await DeviceAsync(),
            "ImageFilters" => await ImageFiltersAsync(),
            "Models" => await ModelsAsync(),
            "CanvasPresentation" => await CanvasPresentationAsync(),
            "TwoDShapes" => await TwoDAsync("shapes", Lumyte.Graphics.GpuFormat.Rgba8Unorm),
            "TwoDImage" => await TwoDAsync("image-path-clip", Lumyte.Graphics.GpuFormat.Rgba8Unorm),
            "TwoDHdrLayer" => await TwoDAsync("hdr-layer", Lumyte.Graphics.GpuFormat.Rgba16Float),
            "ComputeEight" => await ComputeEightAsync(),
            "ComputeMixed" => await ComputeMixedAsync(),
            "ShaderPackage" => await ShaderPackageAsync(),
            "InvalidShaderPackage" => await InvalidShaderPackageAsync(),
            "BufferPool" => await BufferPoolAsync(),
            "TexturePool" => await TexturePoolAsync(),
            "ResourceManagerCompute" => await ResourceManagerComputeAsync(),
            "ResourceManagerPackage" => await ResourceManagerPackageAsync(),
            "IndexedRaster" => await IndexedRasterAsync(),
            "TextureUpload" => await TextureUploadAsync(),
            "MappingLease" => await MappingLeaseAsync(),
            "InvalidMapping" => await InvalidMappingAsync(),
            "InvalidModule" => await InvalidPipelineAsync(true),
            "InvalidEntry" => await InvalidPipelineAsync(false),
            "InvalidBatch" => await InvalidBatchAsync(),
            "Timeline" => await TimelineAsync(),
            "Cancellation" => await CancellationAsync(),
            "IndirectCompute" => await IndirectComputeAsync(),
            "DynamicBindings" => await DynamicBindingsAsync(),
            "SampledTexture" => await SampledTextureAsync(),
            "ConcurrentMapping" => await ConcurrentMappingAsync(),
            "PartialMapping" => await PartialMappingAsync(),
            "RuntimeImportRetry" => await RuntimeImportRetryAsync(),
            "ExactBufferSize" => await ExactBufferSizeAsync(),
            "SynchronousEncodingFailure" => await SynchronousEncodingFailureAsync(),
            _ => throw new ArgumentException("Unknown browser conformance scenario.", nameof(scenario)),
        };
        return JsonSerializer.Serialize(result);
    }
}
