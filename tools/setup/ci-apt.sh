#!/usr/bin/env bash
# Source only on ephemeral Ubuntu CI runners. Never replace host APT sources.
lumyte_apt_options=()
if [[ "${LUMYTE_CI_APT:-0}" == 1 ]]; then
    [[ "${GITHUB_ACTIONS:-}" == true && "${RUNNER_ENVIRONMENT:-}" == github-hosted && "$ID" == ubuntu && "$VERSION_CODENAME" == noble ]] || {
        echo "CI APT routing requires a GitHub-hosted Ubuntu noble environment." >&2
        return 1
    }
    case "$(uname -m)" in
        x86_64) mirror=https://archive.ubuntu.com/ubuntu; security=https://security.ubuntu.com/ubuntu ;;
        aarch64) mirror=https://ports.ubuntu.com/ubuntu-ports; security=$mirror ;;
        *) echo "Unsupported CI APT architecture." >&2; return 1 ;;
    esac
    apt_ci="$LUMYTE_ENV_ROOT/ci-apt"
    mkdir -p "$apt_ci/archives/partial"
    cat > "$apt_ci/sources.list" <<EOF
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $mirror noble main universe restricted multiverse
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $mirror noble-updates main universe restricted multiverse
deb [signed-by=/usr/share/keyrings/ubuntu-archive-keyring.gpg] $security noble-security main universe restricted multiverse
EOF
    # APT checks restored .deb files against fresh signed indexes; stale
    # versions are never forced and signature/hash verification stays enabled.
    lumyte_apt_options=(-o "Dir::Etc::sourcelist=$apt_ci/sources.list"
        -o "Dir::Etc::sourceparts=-" -o "Dir::Cache::archives=$apt_ci/archives"
        -o Binary::apt-get::APT::Keep-Downloaded-Packages=true)
    unset mirror security apt_ci
fi
