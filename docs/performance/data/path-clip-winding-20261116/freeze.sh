#!/usr/bin/env bash
set -euo pipefail
C=$HOME/.cache/lumyte-path-clip-winding
E=$HOME/.cache/pcw-evidence
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env TMPDIR=$HOME/.cache/pcw-tmp
cd "$C"; source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
P=tools/benchmarks/drawing2d-skia-bdn
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release > "$E/frozen-cpu.log" 2>&1
dotnet build "$P" -c Release > "$E/frozen-build.log" 2>&1
dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-quality-compare "$E/quality" > "$E/frozen-quality.log" 2>&1
LUMYTE_RECORD_BENCH=1 LUMYTE_RECORD_TRACE=1 dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --filter '*path200-dynamic*' --artifacts "$E/frozen-profile" > "$E/frozen-profile.log" 2>&1
echo FREEZE_VERIFY_COMPLETE
