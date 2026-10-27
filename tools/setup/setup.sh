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
    "$VCPKG_DOWNLOADS" "$VCPKG_DEFAULT_BINARY_CACHE" "$LUMYTE_NUGET_FEED" \
    "$LUMYTE_ENV_ROOT/dotnet-bootstrap"
case "$(uname -m)" in
    x86_64) mise_arch=x64 ;;
    aarch64) mise_arch=arm64 ;;
    *) echo 'Unsupported CPU architecture.' >&2; exit 1 ;;
esac
# Parse the verified tool metadata with the available .NET SDK. Run outside the
# checkout so its pinned SDK can be installed by mise before global.json applies.
bootstrap_root="$(mktemp -d)"
trap 'rm -rf -- "$bootstrap_root"' EXIT
cp "$repo_root/build/Bootstrap/ReadMise.cs" "$bootstrap_root/ReadMise.cs"
bootstrap_data="$(cd "$bootstrap_root" && XDG_DATA_HOME="$LUMYTE_ENV_ROOT/dotnet-bootstrap" dotnet run --file ReadMise.cs --verbosity quiet --no-launch-profile -- "$script_dir/mise-bootstrap.json" "linux-$mise_arch")"
read -r LUMYTE_MISE_VERSION mise_hash <<< "$bootstrap_data"
rm -rf -- "$bootstrap_root"
trap - EXIT
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
dotnet run --project build -c Release -- --target VerifySdk
mise ls --current
echo 'Setup complete. Run: source tools/setup/activate.sh'
echo 'Then validate with: mise run verify'
