#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
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

# Relocate the ICD library path for packages extracted without installation.
python_command=python3
command -v "$python_command" >/dev/null || { echo 'python3 is required to configure the lavapipe ICD.' >&2; exit 1; }
"$python_command" - "$LUMYTE_ENV_ROOT" <<'PY'
import glob, json, os, sys
root = sys.argv[1]
local = glob.glob(root + '/sysroot/usr/share/vulkan/icd.d/*lvp*.json')
paths = local or glob.glob('/usr/share/vulkan/icd.d/*lvp*.json')
if len(paths) != 1:
    raise SystemExit('Expected one lavapipe ICD; found: ' + repr(paths))
with open(paths[0]) as f:
    data = json.load(f)
if local:
    libs = glob.glob(root + '/sysroot/usr/lib/*/libvulkan_lvp.so')
    if len(libs) != 1:
        raise SystemExit('Cannot locate extracted lavapipe library')
    data['ICD']['library_path'] = libs[0]
with open(root + '/lavapipe.json', 'w') as f:
    json.dump(data, f, indent=2)
    f.write('\n')
PY

"$VCPKG_ROOT/vcpkg" version
