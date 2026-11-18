#!/usr/bin/env bash
# Runtime-only provisioning; bootstrap and product builds belong elsewhere.
set -euo pipefail
role="${1:-native}"
if [[ $# -gt 1 ]]; then
    echo 'Usage: bash tools/setup/install-ci-runtime.sh [native|browser|aot]' >&2
    exit 2
fi
case "$role" in
    native) packages=(libopenal1 xvfb xauth) ;;
    browser|aot) packages=(libopenal1) ;;
    *) echo "Unknown runtime role: $role (expected native, browser, or aot)." >&2; exit 2 ;;
esac
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/environment.sh"
source /etc/os-release
case "$ID" in debian|ubuntu) ;; *) echo "Unsupported distribution: $ID" >&2; exit 1 ;; esac
source "$script_dir/ci-apt.sh"

# Preserve proxy routing as setup-os.sh does. Never edit host sources or
# disable authentication. Update and install use identical routing options.
apt_prefix=()
if [[ "$EUID" != 0 ]]; then
    apt_prefix=(sudo -n --preserve-env=HTTP_PROXY,HTTPS_PROXY,NO_PROXY,http_proxy,https_proxy,no_proxy)
fi
"${apt_prefix[@]}" apt-get "${lumyte_apt_options[@]}" update -o APT::Update::Error-Mode=any
"${apt_prefix[@]}" env DEBIAN_FRONTEND=noninteractive apt-get "${lumyte_apt_options[@]}" install -y --no-install-recommends "${packages[@]}"
