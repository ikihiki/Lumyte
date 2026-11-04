#!/usr/bin/env bash
set -euo pipefail
ROOT=$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)
export LUMYTE_ENV_ROOT="$ROOT/artifacts/dev-env"
export WORKLOAD_REPO_ROOT="$ROOT"
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_gcServer=0
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
source "$ROOT/tools/setup/activate.sh"
SOURCE=$(git -C "$ROOT" rev-parse HEAD)
export LUMYTE_WORKLOAD_REVISION="$SOURCE"
OUT="$ROOT/artifacts/formal-workloads-20261102"
if [ -e "$OUT" ]; then printf 'Output already exists; preserve prior observations: %s\n' "$OUT" >&2; exit 2; fi
mkdir -p "$OUT"
{ date -Iseconds; id; uname -a; dotnet --info; lscpu; printf 'Source=%s TieredCompilation=%s TieredPGO=%s gcServer=%s\n' "$SOURCE" "$DOTNET_TieredCompilation" "$DOTNET_TieredPGO" "$DOTNET_gcServer"; if [ -r /sys/fs/cgroup/cpu.max ]; then cat /sys/fs/cgroup/cpu.max; else printf "cpu.max unavailable on this host\n"; fi; grep Cpus_allowed_list /proc/self/status; sha256sum "$ROOT/tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf"; } > "$OUT/environment.txt"
vulkaninfo --summary > "$OUT/adapter.txt" 2>&1
grep -q 'Radeon 8060S' "$OUT/adapter.txt"
grep -q 'driverName.*radv' "$OUT/adapter.txt"
for variant in main pr105; do
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  revision=fbc8105; if [ "$variant" = pr105 ]; then revision=ffe075c; fi
  test "$(git -C "$TREE" rev-parse HEAD)" = "$(git -C "$ROOT" rev-parse "$revision")"
  test "$(git -C "$TREE" diff --name-only | awk '$0 != "src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj" { n++ } END { print n+0 }')" = 0
  git -C "$ROOT" archive "$SOURCE" tools/benchmarks/drawing2d-incremental | tar -x -C "$TREE"
  (cd "$TREE"; dotnet build tools/benchmarks/drawing2d-incremental -c Release --nologo) > "$OUT/$variant-build.log" 2>&1
  (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --workload-list) > "$OUT/$variant-cases.jsonl" 2>&1
  (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --workload-smoke --suite all --count 200) > "$OUT/$variant-checks.jsonl" 2>&1
  (cd "$TREE"; git rev-parse HEAD; git diff -- src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj; sha256sum tools/benchmarks/drawing2d-incremental/*.cs) > "$OUT/$variant-provenance.txt"
done
# All builds and correctness checks finish before the first timed process.
for item in main:1 pr105:1 pr105:2 main:2; do
  variant=${item%:*}; repeat=${item#*:}
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --workload-cpu --suite all --count 200 --artifacts "$OUT/$variant-run$repeat-cpu") > "$OUT/$variant-run$repeat-cpu.log" 2>&1
  printf 'Completed CPU %s repeat %s\n' "$variant" "$repeat"
  (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --workload-hardware --suite all --count 200 --warmup 41 --samples 120) > "$OUT/$variant-run$repeat-stages.jsonl" 2>&1
  printf 'Completed stages %s repeat %s\n' "$variant" "$repeat"
  for depth in 1 3; do
    (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --workload-hardware --suite all --count 200 --warmup 41 --samples 120 --mode pipeline --frames-in-flight "$depth") > "$OUT/$variant-run$repeat-depth$depth.jsonl" 2>&1
    printf 'Completed depth %s %s repeat %s\n' "$depth" "$variant" "$repeat"
  done
done
