#!/usr/bin/env bash
set -euo pipefail
base=$(realpath "${1:?usage: run-comparison.sh PERF_BASE}")
: "${LUMYTE_ENV_ROOT:?Set the existing verified dev environment root}"
export TMPDIR="${TMPDIR:-$base/tmp}"
mkdir -p "$TMPDIR" "$base/artifacts"
source "$base/direct/tools/setup/activate.sh"
export DOTNET_GCServer=0 DOTNET_GCHeapHardLimit=0x80000000
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_ReadyToRun=0
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
harness=tools/benchmarks/shader-upload-strategies
fingerprint() {
  local variant=$1 output=$2
  (cd "$base/$variant"; sha256sum "$harness"/*.cs "$harness"/*.csproj; find "$harness/bin/Release/net10.0" -type f -name '*.dll' -print0 | sort -z | xargs -0 sha256sum) > "$output"
}
for variant in direct buffered; do
  fingerprint "$variant" "$base/artifacts/$variant-before.sha256"
done
# Frozen independent process order: A1, B1, B2, A2. No builds overlap timing.
for entry in direct:direct-run1:0 buffered:buffered-run1:0 buffered:buffered-run2:0 direct:direct-run2:0; do
  IFS=: read -r variant run baseline <<< "$entry"
  echo "BEGIN $run $(date -Iseconds)"
  mkdir -p "$base/artifacts/$run"
  (cd "$base/$variant"; TRANSFER_BASELINE=$baseline dotnet "$harness/bin/Release/net10.0/ShaderUploadStrategies.Benchmarks.dll" --filter '*' --join --artifacts "$base/artifacts/$run") > "$base/artifacts/$run/launcher.log" 2>&1
  node "$base/direct/$harness/analyze.mjs" "$base" --validate-run "$run" >> "$base/artifacts/$run/launcher.log"
  echo "END $run $(date -Iseconds)"
  fingerprint "$variant" "$base/artifacts/$run/after.sha256"
  diff -u "$base/artifacts/$variant-before.sha256" "$base/artifacts/$run/after.sha256" > "$base/artifacts/$run/fingerprint.diff"
done
for variant in direct buffered; do
  fingerprint "$variant" "$base/artifacts/$variant-after.sha256"
done
