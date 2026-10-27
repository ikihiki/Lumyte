#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source "$script_dir/environment.sh"

: "${LUMYTE_VCPKG_COMMIT:?Run this through mise run setup-native}"
mkdir -p "$VCPKG_DOWNLOADS" "$VCPKG_DEFAULT_BINARY_CACHE"

if [[ ! -d "$VCPKG_ROOT/.git" ]]; then
    [[ ! -e "$VCPKG_ROOT" ]] || { echo "Refusing to overwrite $VCPKG_ROOT" >&2; exit 1; }
    git init "$VCPKG_ROOT"
    git -C "$VCPKG_ROOT" remote add origin https://github.com/microsoft/vcpkg.git
fi
if ! git -C "$VCPKG_ROOT" cat-file -e "$LUMYTE_VCPKG_COMMIT^{commit}" 2>/dev/null; then
    git -C "$VCPKG_ROOT" fetch --depth 1 origin "$LUMYTE_VCPKG_COMMIT"
fi
if [[ "$(git -C "$VCPKG_ROOT" rev-parse HEAD 2>/dev/null || true)" != "$LUMYTE_VCPKG_COMMIT" ]]; then
    [[ -z "$(git -C "$VCPKG_ROOT" status --porcelain)" ]] || { echo 'vcpkg checkout has local changes.' >&2; exit 1; }
    git -C "$VCPKG_ROOT" checkout --detach "$LUMYTE_VCPKG_COMMIT"
fi
bash "$VCPKG_ROOT/bootstrap-vcpkg.sh" -disableMetrics

# Relocate extracted ICD paths through the same .NET build entry point.
dotnet run --project "$repo_root/build" -c Release -- --target ConfigureLavapipe

"$VCPKG_ROOT/vcpkg" version
