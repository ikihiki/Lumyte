# ADR-0027 paired GPU measurements

This driver runs the public Canvas/Scene GPU executable in independent processes. Build and finish correctness gates before starting it. Do not run other builds, tests or benchmarks concurrently. It does not implement or claim the other ADR scenario families.

```sh
source tools/setup/activate.sh
python3 -m unittest discover -s tools/benchmarks/graphics-scenarios -v
unset DOTNET_TieredCompilation DOTNET_TieredPGO COMPlus_TieredCompilation COMPlus_TieredPGO
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json python3 tools/benchmarks/graphics-scenarios/run-pairs.py --update one --locality dispersed --granularity 64 --depth 1 --output artifacts/graphics-benchmarks/local-one-depth1
```

Use `--depth 3` for the separate three-slot profile. Use `--aa` for independent Canvas/Canvas noise observations, not an optimization comparison. Output directories must not already exist; the driver will not overwrite previous observations.

Each variant first receives a 2,000-frame pilot. Its four most recent 50-frame host/GPU median windows must fit the runner's 1.05 stability criterion. The larger recommended warmup is fixed for both variants. Unstable pilots remain explicitly unstable, rather than silently relaxing thresholds. The measured frame count is fixed before formal processes to at least 2,000 and 120% of the larger pilot-derived ten-second budget. Any formal duration shortfall is retained and invalidates formal qualification; there is no favorable-value stopping rule.

Five independent pairs run in alternating A/B and B/A order. Each process produces its own manifest, raw frames, exact/analytic checks and summary. The comparison reports process-level ratios without pooling all frames as independent samples. All measured durations include final GPU drain/retirement. These are headless measurements, not displayed FPS, input-to-display latency or exclusive GPU occupancy.

The default profile is a starting point, not a Cartesian sweep. Select static/one/1%/10%/all, locality, commands per content node and slot depth explicitly. Preserve source hashes, cache budgets, profile and runtime settings when comparing runs. Generated measurements remain under ignored `artifacts/`; do not add them to Git.
