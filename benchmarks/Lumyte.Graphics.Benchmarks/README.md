# Drawing2D performance workloads

Use the existing setup, then run from this project directory. A tiny CsProjGenerator override selects the exact project: upstream BenchmarkDotNet solution-wide discovery follows mise trusted-config symlinks and otherwise finds duplicate projects even from this directory.

```bash
source tools/setup/activate.sh
cd benchmarks/Lumyte.Graphics.Benchmarks
dotnet run -c Release -- --filter '*RecordingBenchmarks*' '*SnapshotBenchmarks*' '*TextBenchmarks*' '*LayerRecordingBenchmarks*' '*PathPreparationBenchmarks*' --artifacts ../../artifacts/rendering-performance/cpu
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json dotnet run -c Release --no-build -- --gpu > ../../artifacts/rendering-performance/native.jsonl
```

CPU benchmarks use BenchmarkDotNet 0.15.8, one process launch, three warmup and twelve measured 150ms iterations; full JSON includes individual measurements, median/p95, dispersion and MemoryDiagnoser allocations. Recording reuses a Canvas and immutable paints/paths, but creates a fresh immutable snapshot per operation. ColorRectangles separately includes paint convenience allocations. Snapshot isolates typed tape construction. Text uses a real Parley layout with DejaVu Sans; override LUMYTE_BENCH_FONT to a reproducible font file. Static excludes layout; dynamic includes shaping, outline extraction and recording.

Native probe uses the default rectangle fast path on a reusable Renderer2D, 800x800 Rgba8Srgb attachment, one cold-renderer sample, five warmup and twenty warm samples. It reports build, prepare, record+Finish, Queue.Submit and submit-to-completion **CPU wall latency**, not GPU timestamps or frame throughput. Command-buffer creation and target barrier are outside record timing; disposal is outside all reported stages. Frames deliberately serialize for resource-lifetime safety and stage isolation. Allocation deltas are process-wide approximate managed bytes, not native/VRAM use. Rectangle draw counts are expectations from the 1024 chunk implementation, not instrumented counters; no-clip solid payload is six Vector4 (96 bytes) per rectangle. Clear adds a render pass, not a draw.

Extended native coverage: run `VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json DOTNET_TieredCompilation=0 dotnet run -c Release --no-build -- --extended`. This executes 512/1536 targets, 50 cold/warmup plus 100 measured frames per direct workload and blur graph; dynamic text changes per frame, static/MTSDF reuse layout/atlas. Atlas creation/upload is separately reported once. Graph cases include per-frame resource/pipeline construction (real integration cost), append, compile, record and actual blur submission, not a retained-resource best case. Throughput compares depth 1/3 with independent attachments and prepared owners retained through each submission; six measured 100-frame blocks after one warmup, final drain included. It reuses one immutable 10,000-rectangle recording, excluding tape build. No timestamp inference.

Do not run CPU/GPU measurements concurrently. Select and verify the intended GPU before use. The hardcoded Radeon ICD guard is specific to the report host, intentionally preventing accidental lavapipe results; adapt only after verifying another host.
