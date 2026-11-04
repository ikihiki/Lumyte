#!/usr/bin/env bash
set -euo pipefail
ROOT=$(git -C "$(dirname "${BASH_SOURCE[0]}")" rev-parse --show-toplevel)
export LUMYTE_ENV_ROOT="$ROOT/artifacts/dev-env"
export DOTNET_TieredCompilation=0
export DOTNET_gcServer=0
export DOTNET_TieredPGO=0
source "$ROOT/tools/setup/activate.sh"
OUT="$ROOT/artifacts/architecture-comparison/results"
SOURCE=$(git -C "$ROOT" rev-parse HEAD)
mkdir -p "$OUT"
{ date -Iseconds; git -C "$ROOT" rev-parse HEAD; dotnet --info; lscpu; uname -a; printf 'TieredCompilation=%s gcServer=%s TieredPGO=%s\n' "$DOTNET_TieredCompilation" "$DOTNET_gcServer" "$DOTNET_TieredPGO"; cat /sys/fs/cgroup/cpu.max 2>/dev/null || true; grep Cpus_allowed_list /proc/self/status; } > "$OUT/environment.txt"
for variant in main pr105; do
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  revision=fbc8105
  if [ "$variant" = pr105 ]; then revision=ffe075c; fi
  if [ ! -f "$TREE/.git" ]; then git -C "$ROOT" worktree add --detach "$TREE" "$revision"; fi
  test "$(git -C "$TREE" rev-parse HEAD)" = "$(git -C "$ROOT" rev-parse "$revision")"
  mkdir -p "$TREE/tools/benchmarks/drawing2d-incremental" "$TREE/artifacts/nuget"
  git -C "$ROOT" archive "$SOURCE" tools/benchmarks/drawing2d-incremental | tar -x -C "$TREE"
  python3 - "$TREE" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1])/'src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj'
s=p.read_text();friend='<InternalsVisibleTo Include="Lumyte.Graphics.Incremental.Benchmarks" />'
if friend not in s:s=s.replace('<InternalsVisibleTo Include="Lumyte.Graphics.Drawing2D.Tests" />','<InternalsVisibleTo Include="Lumyte.Graphics.Drawing2D.Tests" />\n    '+friend)
p.write_text(s)
PY
  (cd "$TREE"; dotnet build tools/benchmarks/drawing2d-incremental -c Release --nologo) > "$OUT/$variant-build.log" 2>&1
  (cd "$TREE"; DRAWING_BASELINE="$variant" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --smoke) > "$OUT/$variant-smoke.jsonl" 2>&1
  (cd "$TREE"; git rev-parse HEAD; git diff -- src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj; sha256sum tools/benchmarks/drawing2d-incremental/*.cs) > "$OUT/$variant-provenance.txt"
done
# All builds and checks finish above. No builds/tests run in parallel with timings.
for item in main:1 pr105:1 pr105:2 main:2; do
  variant=${item%:*}; repeat=${item#*:}
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  (cd "$TREE"; DRAWING_BASELINE="$variant" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --filter '*UpdateBenchmarks*' --artifacts "$OUT/$variant-run$repeat") > "$OUT/$variant-run$repeat.log" 2>&1
  printf 'Completed %s repeat %s\n' "$variant" "$repeat"
done
for variant in main pr105; do
  TREE="$ROOT/artifacts/architecture-comparison/$variant"
  (cd "$TREE"; DRAWING_BASELINE="$variant" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --filter '*ChunkBenchmarks*' '*ConstructionBenchmarks*' --artifacts "$OUT/$variant-secondary") > "$OUT/$variant-secondary.log" 2>&1
  (cd "$TREE"; DRAWING_BASELINE="$variant" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --retention) > "$OUT/$variant-retention.jsonl" 2>&1
  (cd "$TREE"; DRAWING_BASELINE="$variant" VK_DRIVER_FILES="$LUMYTE_LAVAPIPE_ICD" dotnet run --project tools/benchmarks/drawing2d-incremental -c Release --no-build -- --software-integration) > "$OUT/$variant-software.jsonl" 2>&1
  printf 'Completed secondary/retention/software %s\n' "$variant"
done
