# Clip normalization comparison

This additional runner uses the existing BenchmarkDotNet executable and public
Renderer2D/DrawingScene2D APIs. The copied historical Skia comparison sources are
not the baseline measurement for this task. The preserved baseline-pilot log was
zero bytes and supplies no completion or timing evidence.

Set LUMYTE_CLIP_BENCH=1 to select only ClipFrameBenchmarks. Three fixtures:

- rect-control: dynamic recording and fresh preparation of 128 rectangles, no clip.
- mask-dynamic: dynamic recording, one triangle and one fractional rectangular
  clip, CPU mask creation on every timed prepare, then a GPU-completed frame.
- mask-cached: the same static clipped scene after a completed setup frame;
  completion-published prepared assets (including mask storage) are reused.

The runner asserts fresh compilation for dynamic frames and prepared-asset reuse
for cached frames. GlobalSetup warms pipelines and the cached fixture deliberately;
mask creation is NOT hidden in setup for the dynamic fixture. Both variants use
identical source files, target, inputs, completion boundary and job configuration.
No GPU timestamp query is present. Results measure end-to-end host wall latency
through GPU completion and owner retirement, not GPU execution time.

One operation includes recording when dynamic, retained preparation, a fresh
command buffer, target barrier, Record/Finish, queue submission, WaitAsync with a
Completed assertion, command disposal, frame retirement and submission disposal.
No presentation/VSync and no overlap between timed processes.

The bounded job has 3 warmup and 8 measured iterations, a 100 ms target iteration
time, MemoryDiagnoser, full JSON export and no outlier removal. Median/P95 columns
are percentiles of iteration estimates, not individual-frame latency percentiles.
This is a bounded engineering comparison, not a stability-qualified general claim.

Run from each isolated checkout, after source freeze and exact source-hash comparison:

```sh
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
export TMPDIR="$PWD/artifacts/tmp"
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export LUMYTE_CLIP_BENCH=1
dotnet build tools/benchmarks/drawing2d-skia-bdn/Drawing2DSkiaBdn.csproj -c Release
dotnet tools/benchmarks/drawing2d-skia-bdn/bin/Release/net10.0/Drawing2DSkiaBdn.dll --filter '*' --artifacts artifacts/normalized-clips/NAME
```

Predeclared process order: baseline A, candidate A, candidate B, baseline B.
Raw full JSON, process logs, source/binary hashes, hardware identity and the
comparison plan live under artifacts/normalized-clips in the home-backed candidate
checkout. No push, PR or merge is performed.
