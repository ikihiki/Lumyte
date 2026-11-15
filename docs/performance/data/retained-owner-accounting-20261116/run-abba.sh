#!/usr/bin/env bash
set -euo pipefail
ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
A=${A:-/home/hideaki/owner-accounting-repro-base}
B=${B:-/home/hideaki/owner-accounting-repro-candidate}
E=${E:-$ROOT/artifacts/owner-accounting/reproduction}
BASE=b9c93f001f960221dc13eed5f6edc0bf04914c02
FROZEN=149ab21b9237ed71cbdbd811d63947b495c0b83d
# Main subsequently retired this runner. Reproduce the frozen experiment without
# restoring it into the active tree or relabeling old measurements as current-main.
for pair in "$A:$BASE" "$B:$FROZEN"; do
 D=${pair%:*}; SHA=${pair##*:}
 if [[ ! -d "$D" ]]; then git -C "$ROOT" worktree add --detach "$D" "$SHA"; fi
 [[ $(git -C "$D" rev-parse HEAD) == "$SHA" ]] || { echo "Unexpected checkout: $D" >&2; exit 1; }
 git -C "$D" diff --exit-code -- src
 done
for file in Program.cs RecordFrameBenchmarks.cs OwnerAccountingBenchmarks.cs; do
 git -C "$ROOT" show "$FROZEN:tools/benchmarks/drawing2d-skia-bdn/$file" > "$A/tools/benchmarks/drawing2d-skia-bdn/$file"
done
P=tools/benchmarks/drawing2d-skia-bdn
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
export TMPDIR=/home/hideaki/.cache/oa-tmp
mkdir -p "$E" "$TMPDIR"
cd "$B"; source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
unset LUMYTE_RECORD_BENCH LUMYTE_RECORD_TRACE LUMYTE_CLIP_BENCH LUMYTE_CLIP_CPU_BENCH
export LUMYTE_OWNER_BENCH=1
# Two variants, same frozen harness. Build serially before any timing.
for v in baseline candidate; do
 [[ $v == baseline ]] && D=$A || D=$B
 (cd "$D"; dotnet build "$P/Drawing2DSkiaBdn.csproj" -c Release) > "$E/$v-build.log" 2>&1
 (cd "$D"; sha256sum "$P"/*.cs "$P"/*.csproj) > "$E/$v-harness.sha256"
 (cd "$D"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f -print0 | sort -z | xargs -0 sha256sum) > "$E/$v-binaries-before.sha256"
 (cd "$D"; sha256sum src/Graphics/Lumyte.Graphics.Drawing2D/Execution/RetainedDrawingCache2D.cs) > "$E/$v-source.sha256"
done
cmp "$E/baseline-harness.sha256" "$E/candidate-harness.sha256"
# Predeclared 32 completed-frame cases: four scenarios x 0/2 explicit stats x ABBA.
# Eight further direct-read diagnostics are not frames and cannot establish application FPS.
for run in A1 B1 B2 A2; do
 [[ $run == A* ]] && cd "$A" || cd "$B"
 echo "START $run $(date -Is)"
 dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --keepFiles --filter '*OwnerAccountingFrameBenchmarks*rect1000*' '*OwnerAccountingFrameBenchmarks*path200-dynamic*' '*OwnerAccountingFrameBenchmarks*mixed200-dynamic*' --artifacts "$E/frames/$run" > "$E/$run.log" 2>&1
 dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --keepFiles --filter '*OwnerAccountingStatisticsBenchmarks*' --artifacts "$E/reads/$run" > "$E/reads-$run.log" 2>&1
 echo "END $run $(date -Is)"
done
for v in baseline candidate; do
 [[ $v == baseline ]] && D=$A || D=$B
 (cd "$D"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f -print0 | sort -z | xargs -0 sha256sum) > "$E/$v-binaries-after.sha256"
 cmp "$E/$v-binaries-before.sha256" "$E/$v-binaries-after.sha256"
done
echo ABBA_COMPLETE
