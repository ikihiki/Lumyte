#!/usr/bin/env bash
set -euo pipefail
E=$HOME/.cache/pcw-evidence
A=$HOME/.cache/lumyte-pcw-baseline
B=$HOME/.cache/lumyte-path-clip-winding
P=tools/benchmarks/drawing2d-skia-bdn
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env TMPDIR=$HOME/.cache/pcw-tmp
cd "$B"; source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
unset LUMYTE_RECORD_TRACE LUMYTE_RECORD_BENCH LUMYTE_CLIP_CPU_BENCH
for v in baseline candidate; do
 [[ $v == baseline ]] && D=$A || D=$B
 (cd "$D"; sha256sum "$P"/*.cs "$P"/*.csproj) > "$E/$v-harness.sha256"
 (cd "$D"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f -print0 | sort -z | xargs -0 sha256sum) > "$E/$v-binaries-before.sha256"
done
cmp "$E/baseline-harness.sha256" "$E/candidate-harness.sha256"
for run in A1 B1 B2 A2; do
 [[ $run == A* ]] && cd "$A" || cd "$B"
 echo "START $run $(date -Is)"
 LUMYTE_RECORD_BENCH=1 dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --filter '*' --artifacts "$E/abba/$run" > "$E/$run.log" 2>&1
 LUMYTE_CLIP_CPU_BENCH=1 dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --filter '*' --artifacts "$E/micro/$run" > "$E/micro-$run.log" 2>&1
 echo "END $run $(date -Is)"
done
for v in baseline candidate; do
 [[ $v == baseline ]] && D=$A || D=$B
 (cd "$D"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f -print0 | sort -z | xargs -0 sha256sum) > "$E/$v-binaries-after.sha256"
 cmp "$E/$v-binaries-before.sha256" "$E/$v-binaries-after.sha256"
done
cd "$B"
node "$P/summarize-records.mjs" "$E/abba" "$E/summary.json" > "$E/comparisons.json"
echo ABBA_COMPLETE
