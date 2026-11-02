#!/usr/bin/env bash
set -euo pipefail
ROOT=$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)
export LUMYTE_ENV_ROOT="$ROOT/artifacts/dev-env"
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_gcServer=0
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
source "$ROOT/tools/setup/activate.sh"
SOURCE=$(git -C "$ROOT" rev-parse HEAD)
OUT="$ROOT/artifacts/architecture-comparison/hardware-results"
mkdir -p "$OUT"
{ date -Iseconds; id; uname -a; dotnet --info; lscpu; printf 'Source=%s\n' "$SOURCE"; } > "$OUT/environment.txt"
vulkaninfo --summary > "$OUT/adapter.txt" 2>&1
grep -q 'Radeon 8060S' "$OUT/adapter.txt"
grep -q 'driverName.*radv' "$OUT/adapter.txt"
for variant in main pr105; do
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  revision=fbc8105; if [ "$variant" = pr105 ]; then revision=ffe075c; fi
  test "$(git -C "$TREE" rev-parse HEAD)" = "$(git -C "$ROOT" rev-parse "$revision")"
  git -C "$ROOT" archive "$SOURCE" tools/benchmarks/drawing2d-incremental | tar -x -C "$TREE"
  (cd "$TREE"; dotnet build tools/benchmarks/drawing2d-incremental -c Release --nologo) > "$OUT/$variant-build.log" 2>&1
  (cd "$TREE"; dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --smoke) > "$OUT/$variant-smoke.jsonl" 2>&1
  (cd "$TREE"; git rev-parse HEAD; sha256sum tools/benchmarks/drawing2d-incremental/*.cs) > "$OUT/$variant-provenance.txt"
done
# All builds/checks precede measured processes. Do not run unrelated tests alongside this loop.
index=0
for item in main:1 pr105:1 pr105:2 main:2; do
  variant=${item%:*}; repeat=${item#*:}
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  reverse=$((index % 2))
  (cd "$TREE"; DRAWING_BASELINE="$variant" HARDWARE_REVERSE="$reverse" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --hardware-integration) > "$OUT/$variant-run$repeat.jsonl" 2>&1
  printf 'Completed hardware %s repeat %s reverse=%s\n' "$variant" "$repeat" "$reverse"
  index=$((index + 1))
done
