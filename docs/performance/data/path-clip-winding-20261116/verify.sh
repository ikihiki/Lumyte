#!/usr/bin/env bash
set -euo pipefail
export LUMYTE_ENV_ROOT=/home/hideaki/.openclaw/workspace/Lumyte/artifacts/dev-env TMPDIR=/home/hideaki/.cache/pcw-tmp
C=$HOME/.cache/lumyte-path-clip-winding
A=$HOME/.cache/lumyte-pcw-baseline
E=$HOME/.cache/pcw-evidence
cd "$C"
source tools/setup/activate.sh
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
P=tools/benchmarks/drawing2d-skia-bdn
cp "$C/$P/ClipCpuBenchmarks.cs" "$A/$P/ClipCpuBenchmarks.cs"
cp "$C/$P/Program.cs" "$A/$P/Program.cs"
dotnet test tests/Graphics/Lumyte.Graphics.Drawing2D.Tests -c Release --logger 'trx;LogFileName=pcw-final-cpu.trx' > "$E/final-cpu.log" 2>&1
dotnet test tests/Graphics/Lumyte.Graphics.RenderGraph.Tests -c Release > "$E/final-graph.log" 2>&1
dotnet build "$P" -c Release > "$E/final-build.log" 2>&1
cd "$A"
dotnet build "$P" -c Release > "$E/final-baseline-build.log" 2>&1
dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-quality-write "$E/quality" > "$E/quality-baseline.log" 2>&1
cd "$C"
dotnet "$P/bin/Release/net10.0/Drawing2DSkiaBdn.dll" --record-quality-compare "$E/quality" > "$E/quality-candidate.log" 2>&1
LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --filter 'FullyQualifiedName~NormalizedClipSamples|FullyQualifiedName~SingularFoldedClipPaint' > "$E/gpu-hardware.log" 2>&1
VK_DRIVER_FILES=/usr/share/vulkan/icd.d/lvp_icd.json LUMYTE_GRAPHICS_GPU_TESTS=1 dotnet test tests/Graphics/Lumyte.Graphics.Tests -c Release --no-build --filter 'FullyQualifiedName~NormalizedClipSamples|FullyQualifiedName~SingularFoldedClipPaint' > "$E/gpu-software.log" 2>&1
echo VERIFY_COMPLETE
