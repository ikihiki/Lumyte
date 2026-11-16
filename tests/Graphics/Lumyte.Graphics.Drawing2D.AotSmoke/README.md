# Strict native renderer smoke

This executable instantiates Standard, SparseStripRenderer2D and ComputeRenderer2D directly (no rejection-as-success or renderer fallback), uploads a raw quadratic fill with solid paint, appends it to the shared RenderGraph, applies ImageEffects opacity, submits, waits and checks an Rgba16Float readback against red/alpha 0.5. It does not by itself qualify image sampling, clips, strokes or retained concurrency; the native oracle tests cover those separately.

From the repository's pinned .NET/Slang/DXC development environment:

```sh
dotnet publish tests/Graphics/Lumyte.Graphics.Drawing2D.AotSmoke -c Release -p:PublishAot=true -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true -o artifacts/renderer-verification/stage-aot
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json artifacts/renderer-verification/stage-aot/Lumyte.Graphics.Drawing2D.AotSmoke
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json artifacts/renderer-verification/stage-aot/Lumyte.Graphics.Drawing2D.AotSmoke wgpu
```

Both strict warning gates remain enabled. Vulkan uses a native OS/app-base library context rather than Silk's Assembly.Location/CodeBase/DependencyContext asset resolver; no IL3000/IL3002 suppression is used. Reference-free shader-data streams do not force compute/raster passes into a single native table; only streams with embedded reference slots retain transferred table identity.

Hardware oracle matrix after the normal Fallout Compile runner:

```sh
dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~AlternativeRenderer|FullyQualifiedName~ComputeNonuniformStrokeOracle|FullyQualifiedName~StandardCurveGraphOracle|FullyQualifiedName~RendererStrategiesAsync|FullyQualifiedName~VulkanFixedRecordsAsync|FullyQualifiedName~VulkanNormalizedClipSamplesAsync|FullyQualifiedName~VulkanImagesAndGlyphRepresentationsAsync" --logger "trx;LogFileName=stage-gpu-final.trx" --results-directory artifacts/renderer-verification
```

Set LUMYTE_GRAPHICS_GPU_TESTS=1 and select the hardware ICD, otherwise these opt-in tests may skip. Passing skipped tests is not native qualification. The nonuniform stroke oracle explicitly observes eleven actual compute passes and independently asserts mask 0x0FF0 / RGBA(0.5) at pixel (32,8).
