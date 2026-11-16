#!/usr/bin/env bash
set -euo pipefail
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
candidate=/home/hideaki/incremental-owner-ledger
baseline=/home/hideaki/incremental-owner-ledger-base
report="$candidate/docs/performance/data/incremental-owner-ledger-20261116"
source "$candidate/tools/setup/activate.sh"
for run in A1 B1 B2 A2; do
    if [[ "$run" == A* ]]; then root="$baseline"; else root="$candidate"; fi
    project="$root/benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark/Lumyte.Graphics.Drawing2D.Benchmark.csproj"
    dll="$root/benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark/bin/Release/net10.0/Lumyte.Graphics.Drawing2D.Benchmark.dll"
    export LUMYTE_BENCHMARK_PROJECT="$project"
    cd "$root"
    {
        date -Is
        git rev-parse HEAD
        sha256sum "$dll" "$root/src/Graphics/Lumyte.Graphics.Drawing2D/bin/Release/net10.0/Lumyte.Graphics.Drawing2D.dll"
        sha256sum "$root/benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark/Retained/OwnerAccounting"*Benchmarks.cs
    } > "$report/raw/$run-provenance-before.txt"
    timeout 420s dotnet "$dll" --mode compare --filter '*OwnerAccounting*' --launchCount 1 --warmupCount 3 --iterationCount 10 --keepFiles --artifacts "$report/raw/$run" > "$report/raw/$run.log" 2>&1
    {
        date -Is
        sha256sum "$dll" "$root/src/Graphics/Lumyte.Graphics.Drawing2D/bin/Release/net10.0/Lumyte.Graphics.Drawing2D.dll"
        find "$root/benchmarks/Graphics/Lumyte.Graphics.Drawing2D.Benchmark/bin/Release/net10.0" -mindepth 2 -type f -name '*.dll' -print0 | sort -z | xargs -0 -r sha256sum
    } > "$report/raw/$run-provenance-after.txt"
done
