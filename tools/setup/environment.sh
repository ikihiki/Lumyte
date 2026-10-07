#!/usr/bin/env bash
# Linux environment used by the bootstrap and activation wrappers.
_lumyte_repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
export LUMYTE_ENV_ROOT="${LUMYTE_ENV_ROOT:-$_lumyte_repo_root/artifacts/dev-env}"
export DOTNET_CLI_HOME="$LUMYTE_ENV_ROOT/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export NUGET_PACKAGES="$LUMYTE_ENV_ROOT/nuget-packages"
export MESA_SHADER_CACHE_DIR="$LUMYTE_ENV_ROOT/mesa-cache"
export VCPKG_ROOT="$LUMYTE_ENV_ROOT/vcpkg"
export VCPKG_DOWNLOADS="$LUMYTE_ENV_ROOT/vcpkg-downloads"
export VCPKG_DEFAULT_BINARY_CACHE="$LUMYTE_ENV_ROOT/vcpkg-binary-cache"
export VCPKG_DISABLE_METRICS=1
export VCPKG_MAX_CONCURRENCY="${VCPKG_MAX_CONCURRENCY:-2}"
export LUMYTE_NUGET_FEED="$_lumyte_repo_root/artifacts/nuget"
export MISE_DATA_DIR="$LUMYTE_ENV_ROOT/mise/data"
export MISE_CACHE_DIR="$LUMYTE_ENV_ROOT/mise/cache"
export MISE_STATE_DIR="$LUMYTE_ENV_ROOT/mise/state"
export MISE_CONFIG_DIR="$LUMYTE_ENV_ROOT/mise/config"
export MISE_DISABLE_UPDATE_WARNING=1

if [[ -d "$LUMYTE_ENV_ROOT/sysroot/usr/lib" ]]; then
    _lumyte_arch="$(gcc -dumpmachine)"
    export LD_LIBRARY_PATH="$LUMYTE_ENV_ROOT/sysroot/usr/lib/$_lumyte_arch:$LUMYTE_ENV_ROOT/sysroot/lib/$_lumyte_arch${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export PKG_CONFIG_PATH="$LUMYTE_ENV_ROOT/sysroot/usr/lib/$_lumyte_arch/pkgconfig:$LUMYTE_ENV_ROOT/sysroot/usr/share/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
    export LUMYTE_VULKAN_INCLUDE_DIR="$LUMYTE_ENV_ROOT/sysroot/usr/include"
    export LUMYTE_VULKAN_LIBRARY="$LUMYTE_ENV_ROOT/sysroot/usr/lib/$_lumyte_arch/libvulkan.so"
    # APT may reuse an already-installed loader instead of downloading it. The
    # extracted development symlink then has no target inside the local tree.
    if [[ ! -f "$LUMYTE_VULKAN_LIBRARY" ]]; then
        export LUMYTE_VULKAN_LIBRARY="/usr/lib/$_lumyte_arch/libvulkan.so.1"
    fi
fi

# Verification uses this path explicitly; do not globally override Vulkan drivers.
export LUMYTE_LAVAPIPE_ICD="$LUMYTE_ENV_ROOT/lavapipe.json"
unset _lumyte_repo_root _lumyte_arch
