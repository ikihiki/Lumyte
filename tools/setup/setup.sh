#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
source "$script_dir/environment.sh"

bash "$script_dir/setup-os.sh"
source "$script_dir/environment.sh"
export PATH="$LUMYTE_ENV_ROOT/mise-bin:$VCPKG_ROOT:$LUMYTE_ENV_ROOT/sysroot/usr/bin:$PATH"
mkdir -p "$LUMYTE_ENV_ROOT/mise-bin" "$MISE_DATA_DIR" "$MISE_CACHE_DIR" \
    "$MISE_STATE_DIR" "$MISE_CONFIG_DIR" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" \
    "$VCPKG_DOWNLOADS" "$VCPKG_DEFAULT_BINARY_CACHE" "$LUMYTE_NUGET_FEED"
case "$(uname -m)" in
    x86_64) mise_arch=x64 ;;
    aarch64) mise_arch=arm64 ;;
    *) echo 'Unsupported CPU architecture.' >&2; exit 1 ;;
esac
read -r LUMYTE_MISE_VERSION mise_hash < <(python3 - "$script_dir/mise-bootstrap.json" "$mise_arch" <<'BOOTSTRAP'
import json, sys
config = json.load(open(sys.argv[1]))
print(config['version'], config['platforms']['linux-' + sys.argv[2]]['sha256'])
BOOTSTRAP
)
mise_bin="$LUMYTE_ENV_ROOT/mise-bin/mise"
if [[ ! -x "$mise_bin" ]] || [[ "$("$mise_bin" --version | cut -d ' ' -f 1)" != "$LUMYTE_MISE_VERSION" ]]; then
    archive="$LUMYTE_ENV_ROOT/mise-$LUMYTE_MISE_VERSION-$mise_arch.tar.gz"
    curl --fail --location --retry 3 --proto '=https' --tlsv1.2 \
        "https://github.com/jdx/mise/releases/download/v$LUMYTE_MISE_VERSION/mise-v$LUMYTE_MISE_VERSION-linux-$mise_arch.tar.gz" \
        --output "$archive"
    printf '%s  %s\n' "$mise_hash" "$archive" | sha256sum --check --strict
    tar -xzf "$archive" --strip-components=2 -C "$LUMYTE_ENV_ROOT/mise-bin" mise/bin/mise
    rm -- "$archive"
fi
cd "$repo_root"
mise trust "$repo_root/mise.toml"
mise install --locked
mise reshim
mise run setup-native
source "$script_dir/activate.sh"
python3 - "$repo_root" <<'CHECK'
import json, pathlib, subprocess, sys
root = pathlib.Path(sys.argv[1])
sdk = json.loads((root / 'global.json').read_text())['sdk']['version']
assert subprocess.check_output(['mise', 'current', 'http:dotnet'], text=True).strip() == sdk, 'mise.toml and global.json SDK versions differ'
assert subprocess.check_output(['dotnet', '--version'], text=True).strip() == sdk
CHECK
mise ls --current
echo 'Setup complete. Run: source tools/setup/activate.sh'
echo 'Then validate with: mise run verify'
