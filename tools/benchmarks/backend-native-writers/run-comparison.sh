#!/usr/bin/env bash
set -euo pipefail
# Explicit parent freeze gate: this script must never be used during implementation.
[[ "${BACKEND_WRITERS_FINAL_FROZEN:-}" == yes ]] || { echo 'Parent must freeze final implementation and stop concurrent work first.' >&2; exit 2; }
a=$(realpath "${1:?baseline owned worktree}")
b=$(realpath "${2:?final candidate owned worktree}")
out=$(realpath -m "${3:?local artifacts directory}")
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
: "${LUMYTE_ENV_ROOT:?existing tools/setup environment root required}"
mkdir -p "$out" "$out/tmp"
export TMPDIR="$out/tmp"
source "$a/tools/setup/activate.sh"
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_ReadyToRun=0 DOTNET_GCServer=0
[[ "$(git -C "$a" rev-parse HEAD)" == f27e4b29d02a7e5d78c37b91dc94d33881264e2b ]] || exit 3
fingerprint() { (cd "$source_dir"; sha256sum *.cs *.csproj *.sh) ; }
fingerprint > "$out/harness.sha256"
{ date -Iseconds; uname -a; lscpu; dotnet --info; env | sort | grep -E '^(DOTNET|VK_|LD_LIBRARY_PATH|PATH=)'; git -C "$a" rev-parse HEAD; git -C "$b" rev-parse HEAD; git -C "$b" diff --binary | sha256sum; } > "$out/metadata.txt"
vulkaninfo --summary > "$out/vulkaninfo.txt" 2>&1
grep -q 'Radeon 8060S' "$out/vulkaninfo.txt"
# Two copies outside either source root, byte-identical sources; independent obj/bin.
for variant in baseline candidate; do
  mkdir -p "$out/$variant/harness"
  cp "$source_dir/"*.cs "$source_dir/"*.csproj "$out/$variant/harness/"
  root="$a"; [[ "$variant" == candidate ]] && root="$b"
  # MSBuild imports this environment property in launcher and BDN-generated builds.
  export LumyteProjectRoot="$root"
  dotnet build "$out/$variant/harness/BackendNativeWriters.csproj" -c Release -m:1 -p:LumyteProjectRoot="$root" -p:RunAnalyzersDuringBuild=false > "$out/$variant/build.log" 2>&1
  dotnet "$out/$variant/harness/bin/Release/net10.0/BackendNativeWriters.dll" --verify > "$out/$variant/verify.log" 2>&1
  find "$root/src" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z | xargs -0 sha256sum > "$out/$variant/source.sha256"
  find "$out/$variant/harness/bin/Release/net10.0" -type f -name '*.dll' -print0 | sort -z | xargs -0 sha256sum > "$out/$variant/binaries.sha256"
done
for item in baseline:A1 candidate:B1 candidate:B2 baseline:A2; do
  IFS=: read -r variant name <<< "$item"
  root="$a"; [[ "$variant" == candidate ]] && root="$b"
  export LumyteProjectRoot="$root"
  export BACKEND_WRITERS_PROJECT="$out/$variant/harness/BackendNativeWriters.csproj"
  dotnet "$out/$variant/harness/bin/Release/net10.0/BackendNativeWriters.dll" --filter '*' --join --artifacts "$out/$name" > "$out/$name.log" 2>&1
  find "$out/$variant/harness/bin/Release/net10.0" -type f -name '*.dll' -print0 | sort -z | xargs -0 sha256sum > "$out/$name-binaries.sha256"
  diff -u "$out/$variant/binaries.sha256" "$out/$name-binaries.sha256"
  find "$root/src" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 | sort -z | xargs -0 sha256sum > "$out/$name-source.sha256"
  diff -u "$out/$variant/source.sha256" "$out/$name-source.sha256"
  fingerprint > "$out/$name-harness.sha256"
  diff -u "$out/harness.sha256" "$out/$name-harness.sha256"
done
