# Graphics integration GPU benchmarks

This executable measures **amortized wall time per completed headless logical frame** on real Vulkan hardware, not individual frame latency. It does not measure GPU timestamps, presentation FPS, scanout or VSync. Unsupported D3D/WASM backends are not represented by fake cases.

The twelve cases are 1,024 / 10,000 opaque non-overlapping rectangles × static immutable input reuse / one rectangle changed / every rectangle changed × one / three frames in flight. The fixed target is 256×256 Rgba8Srgb. Each rectangle occupies one pixel in a 128-column grid; therefore count changes actual content coverage, not attachment dimensions.

Every invocation executes exactly six logical frames, and BenchmarkDotNet uses OperationsPerInvoke=6. Both queue depths execute identical work. The asynchronous benchmark includes public Renderer2D preparation, command creation, barriers, Record, Finish, Queue.Submit, explicit WaitAsync, completion-safe command/prepared/submission disposal, and the final drain. It never uses Task.Run or a custom timer. Each slot owns its own target/view, command buffer, prepared drawing and submission. A slot is retired before reuse. Renderer, target creation and initial warmup are outside measurement; this is a warm renderer, not a fresh-process/cold-renderer claim.

Static input recording is outside measurement. Both changing profiles reconstruct Canvas2D and its immutable drawing inside every logical frame. OneChanged changes only the last rectangle's color; it is **not** a retained one-element GPU upload optimization. All profiles prepare and upload a complete drawing every frame. Dynamic colors alternate red/green, ending with frame 5; static ends red.

Global setup and cleanup perform untimed readback. The ordinary opt-in xUnit test runs all twelve cases twice, checks six completed frames and the bounded slot count after each invocation, and checks all 65,536 final-frame pixels against independent analytic red/green/black opaque expectations, including every drawing batch and clear area.

## Hardware gate

GPU initialization occurs only in GlobalSetup, not catalog/list discovery. Execution requires both:

- LUMYTE_GRAPHICS_INTEGRATION_GPU=1
- VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json

The guard independently enumerates physical-device index zero using public Silk.NET Vulkan APIs, requires exactly one adapter, and checks integrated Radeon 8060S vendor 0x1002 / device 0x1586 before creating VulkanDevice at index zero. No reflection or software fallback. The test is genuinely skipped without opt-in; an incorrect manifest or hardware identity with opt-in fails.

The category build owns BenchmarkGraphicsIntegration separately from CPU/default targets. Use the shared executable --catalog interface for untimed discovery; only Screening, Comparison and Diagnostic are purpose categories. Timing campaigns must run without competing builds/tests.
