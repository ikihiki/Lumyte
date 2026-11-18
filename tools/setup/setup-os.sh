#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/environment.sh"

if [[ "$(uname -s)" != Linux ]]; then
    echo 'This setup supports Debian/Ubuntu Linux only.' >&2
    exit 1
fi
source /etc/os-release
case "$ID" in debian|ubuntu) ;; *) echo "Unsupported distribution: $ID" >&2; exit 1 ;; esac
case "$(uname -m)" in
    x86_64) dotnet_arch=x64 ;;
    aarch64) dotnet_arch=arm64 ;;
    *) echo 'Unsupported CPU architecture.' >&2; exit 1 ;;
esac

packages=(build-essential pkg-config curl ca-certificates git
    unzip zip tar python3 fontconfig fonts-noto-cjk libicu-dev libssl-dev zlib1g-dev libvulkan-dev
    mesa-vulkan-drivers vulkan-tools)

# Bounded constants only: never expose raw environment, paths or source/proxy data.
# Diagnostic I/O must not turn acquisition success/failure into a different result.
report_acquisition_route() {
    local gha hosted os arch
    case "${GITHUB_ACTIONS:-}" in true) gha=yes ;; '') gha=unknown ;; *) gha=no ;; esac
    case "${RUNNER_ENVIRONMENT:-}" in github-hosted) hosted=github ;; self-hosted) hosted=self ;; '') hosted=unset ;; *) hosted=other ;; esac
    case "$ID" in ubuntu) os=ubuntu ;; debian) os=debian ;; *) os=other ;; esac
    case "$dotnet_arch" in x64) arch=x64 ;; arm64) arch=arm64 ;; *) arch=other ;; esac
    printf 'APT acquisition route: route=%s reason=%s gha=%s hosted=%s os=%s arch=%s privilege=%s pyProbe=%s\n' \
        "$1" "$2" "$gha" "$hosted" "$os" "$arch" "$3" "$4" >&2 || :
}

# Only explicitly hosted Ubuntu x64 with an already available privileged Python
# selects private sources. Missing/old Python or other runners keep native APT.
# Keep native package/signature/version policy and the original rootless path.
install_system_packages() {
    local privilege reason py_probe
    if [[ "$#" == 0 ]]; then privilege=root; else privilege=sudo; fi
    if [[ "${GITHUB_ACTIONS:-}" == true && "${RUNNER_ENVIRONMENT:-}" == github-hosted && "$ID" == ubuntu && "$dotnet_arch" == x64 ]] \
        && "$@" /bin/sh -c 'command -v python3 >/dev/null 2>&1 && python3 -c "import sys; sys.exit(sys.version_info < (3, 8))"'; then
        report_acquisition_route helper_selected eligible "$privilege" compatible
        "$@" python3 "$script_dir/ci-apt-sources.py" "${packages[@]}"
    else
        py_probe=not-run
        if [[ "${GITHUB_ACTIONS:-}" != true ]]; then reason=not_github_actions
        elif [[ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]]; then reason=not_github_hosted
        elif [[ "$ID" != ubuntu || "$dotnet_arch" != x64 ]]; then reason=unsupported_os_arch
        else reason=python_probe_unavailable_or_old; py_probe=not-compatible
        fi
        report_acquisition_route native "$reason" "$privilege" "$py_probe"
        "$@" apt-get update -o APT::Update::Error-Mode=any
        "$@" env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${packages[@]}"
    fi
}

if [[ "${LUMYTE_SETUP_ROOTLESS:-0}" != 1 ]] && [[ "$EUID" == 0 ]]; then
    install_system_packages
elif [[ "${LUMYTE_SETUP_ROOTLESS:-0}" != 1 ]] && command -v sudo >/dev/null && sudo -n true 2>/dev/null; then
    # Preserve only proxy routing settings; sudo normally drops these, breaking
    # package retrieval in managed remote environments.
    sudo_proxy=(sudo -n --preserve-env=HTTP_PROXY,HTTPS_PROXY,NO_PROXY,http_proxy,https_proxy,no_proxy)
    install_system_packages "${sudo_proxy[@]}"
else
    report_acquisition_route rootless rootless rootless not-run
    # Bootstrap requirements are normally present in cloud images. Fail clearly
    # rather than replacing signature or TLS verification with an unsafe route.
    for command in /usr/bin/apt-get dpkg-deb curl git gcc g++; do
        command -v "$command" >/dev/null || { echo "Rootless setup requires $command" >&2; exit 1; }
    done
    apt_root="$LUMYTE_ENV_ROOT/apt"
    mkdir -p "$apt_root/lists/partial" "$apt_root/archives/partial" "$apt_root/empty" "$LUMYTE_ENV_ROOT/sysroot"
    if [[ "$ID" == debian ]]; then
        keyring=/usr/share/keyrings/debian-archive-keyring.gpg
        mirror=https://deb.debian.org/debian
        security_mirror=https://security.debian.org/debian-security
        security_suite="$VERSION_CODENAME-security"
    else
        keyring=/usr/share/keyrings/ubuntu-archive-keyring.gpg
        if [[ "$dotnet_arch" == x64 ]]; then
            mirror=https://archive.ubuntu.com/ubuntu
            security_mirror=https://security.ubuntu.com/ubuntu
        else
            mirror=https://ports.ubuntu.com/ubuntu-ports
            security_mirror="$mirror"
        fi
        security_suite="$VERSION_CODENAME-security"
    fi
    [[ -r "$keyring" ]] || { echo "Missing trusted archive keyring: $keyring" >&2; exit 1; }
    components=main
    [[ "$ID" != ubuntu ]] || components='main universe'
    cat > "$apt_root/sources.list" <<EOF
deb [signed-by=$keyring] $mirror $VERSION_CODENAME $components
deb [signed-by=$keyring] $mirror $VERSION_CODENAME-updates $components
deb [signed-by=$keyring] $security_mirror $security_suite $components
EOF
    # Load a private config before APT reads configuration directories. No host
    # hooks, lists, cache or package database are modified in rootless mode.
    cat > "$apt_root/apt.conf" <<EOF
Dir::Etc::parts "$apt_root/empty";
Dir::Etc::main "$apt_root/empty/apt.conf";
Dir::Etc::sourcelist "$apt_root/sources.list";
Dir::Etc::sourceparts "$apt_root/empty";
Dir::State::lists "$apt_root/lists";
Dir::State::extended_states "$apt_root/extended_states";
Dir::Cache::archives "$apt_root/archives";
Dir::Cache::pkgcache "";
Dir::Cache::srcpkgcache "";
Debug::NoLocking "true";
APT::Sandbox::User "$(id -un)";
EOF
    APT_CONFIG="$apt_root/apt.conf" /usr/bin/apt-get update -o APT::Update::Error-Mode=any
    APT_CONFIG="$apt_root/apt.conf" /usr/bin/apt-get install -y --download-only --no-install-recommends "${packages[@]}"
    shopt -s nullglob
    for package in "$apt_root/archives/"*.deb; do
        dpkg-deb --extract "$package" "$LUMYTE_ENV_ROOT/sysroot"
    done
    shopt -u nullglob
fi

mkdir -p "$LUMYTE_ENV_ROOT/fontconfig" "$LUMYTE_ENV_ROOT/font-cache"
cat > "$LUMYTE_ENV_ROOT/fontconfig/fonts.conf" <<EOF
<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <dir>/usr/share/fonts</dir>
  <dir>$LUMYTE_ENV_ROOT/sysroot/usr/share/fonts</dir>
  <cachedir>$LUMYTE_ENV_ROOT/font-cache</cachedir>
</fontconfig>
EOF
