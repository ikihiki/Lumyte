#!/usr/bin/env bash
set -euo pipefail
ROOT=$HOME/.cache/lumyte-fixed-records-main-20261115
B="$ROOT/baseline"
C=/home/hideaki/.cache/lumyte-fixed-draw-records
export TMPDIR="$ROOT/tmp" LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env
cd "$C"
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json LUMYTE_RECORD_BENCH=1
unset COUNTS PROFILE_IDS LUMYTE_RECORD_TRACE
P=tools/benchmarks/drawing2d-skia-bdn
for variant in baseline candidate; do
  [[ "$variant" == baseline ]] && dir="$B" || dir="$C"
  ( cd "$dir"; sha256sum "$P"/*.{cs,csproj,mjs} ) > "$ROOT/$variant-sources-before.sha256"
  ( cd "$dir"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.so' -o -name '*.json' \) -print0 | sort -z | xargs -0 sha256sum ) > "$ROOT/$variant-binaries-before.sha256"
done
cmp "$ROOT/baseline-sources-before.sha256" "$ROOT/candidate-sources-before.sha256"
cd "$B"
dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-quality-write "$ROOT/quality" > "$ROOT/quality-baseline.log" 2>&1
cd "$C"
dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-quality-compare "$ROOT/quality" > "$ROOT/quality-candidate.log" 2>&1
python3 - "$ROOT" <<'PY'
import json,sys
from pathlib import Path
r=Path(sys.argv[1]); a=json.load(open(r/'quality/baseline.json')); b=json.load(open(r/'quality/candidate.json'))
assert len(a)==len(b)==256
assert all(x['hash']==y['hash'] and y['differingChannels']==0 and y['maximumChannelDifference']==0 for x,y in zip(a,b))
print('QUALITY EXACT PASS: 8 cases x32 states, all RGBA channels, both rectangle oracles')
PY
for variant in baseline candidate; do
  [[ "$variant" == baseline ]] && dir="$B" || dir="$C"
  cd "$dir"
  for case in rect1000-dynamic rect1000-cached rect341-dynamic rect342-dynamic path200-dynamic path200-cached mixed200-dynamic mixed200-cached; do
    dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-diagnostics "$case" >> "$ROOT/diagnostics-$variant.log" 2>&1
  done
  if [[ "$variant" == candidate ]]; then
    dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-range-diagnostics rect1000-cached > "$ROOT/range-candidate.log" 2>&1
  fi
done
for run in A1 B1 B2 A2; do
  [[ "$run" == A* ]] && cd "$B" || cd "$C"
  echo "START $run $(date -Is)"
  dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --filter '*' --artifacts "$ROOT/abba/$run" > "$ROOT/$run.log" 2>&1
  echo "END $run $(date -Is)"
done
for variant in baseline candidate; do
  [[ "$variant" == baseline ]] && dir="$B" || dir="$C"
  ( cd "$dir"; sha256sum "$P"/*.{cs,csproj,mjs} ) > "$ROOT/$variant-sources-after.sha256"
  ( cd "$dir"; find "$P/bin/Release/net10.0" -maxdepth 1 -type f \( -name '*.dll' -o -name '*.so' -o -name '*.json' \) -print0 | sort -z | xargs -0 sha256sum ) > "$ROOT/$variant-binaries-after.sha256"
  cmp "$ROOT/$variant-sources-before.sha256" "$ROOT/$variant-sources-after.sha256"
  cmp "$ROOT/$variant-binaries-before.sha256" "$ROOT/$variant-binaries-after.sha256"
done
cd "$C"
node "$P/summarize-records.mjs" "$ROOT/abba" "$ROOT/summary.json" > "$ROOT/comparisons.json"
echo "CAMPAIGN COMPLETE $(date -Is)"
