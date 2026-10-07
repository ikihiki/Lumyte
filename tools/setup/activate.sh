#!/usr/bin/env bash
# Source in each new shell. Preserve the caller's shell options.
_lumyte_setup_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$_lumyte_setup_dir/environment.sh"
export PATH="$LUMYTE_ENV_ROOT/mise-bin:$VCPKG_ROOT:$LUMYTE_ENV_ROOT/sysroot/usr/bin:$PATH"
if [[ ! -x "$LUMYTE_ENV_ROOT/mise-bin/mise" ]]; then
    echo 'Run bash tools/setup/setup.sh before activating the environment.' >&2
    return 1
fi
_lumyte_mise_env="$(mise env --shell bash)" || return 1
eval "$_lumyte_mise_env"
_lumyte_dotnet_root="$(mise where http:dotnet)" || return 1
export DOTNET_ROOT="$_lumyte_dotnet_root"
unset _lumyte_setup_dir _lumyte_mise_env _lumyte_dotnet_root
