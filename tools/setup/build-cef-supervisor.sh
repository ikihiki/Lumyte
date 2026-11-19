#!/usr/bin/env bash
# Standalone std-only cleanup owner: no CEF_PATH, CEF cache, Python, or downloads.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export CARGO_HOME="${CARGO_HOME:-$root/artifacts/build/tools/cargo}"
export RUSTUP_HOME="${RUSTUP_HOME:-$root/artifacts/build/tools/rustup}"
export PATH="$CARGO_HOME/bin:$PATH"
cargo +1.99.1 build --locked --offline --release --manifest-path "$root/src/UI/Lumyte.UI.WebView.Cef.Supervisor/Cargo.toml" --target-dir "$root/artifacts/cef-supervisor/cargo"
install -d "$root/artifacts/cef-supervisor/native"
install -m755 "$root/artifacts/cef-supervisor/cargo/release/lumyte-cef-supervisor" "$root/artifacts/cef-supervisor/native/lumyte-cef-supervisor"
printf "%s\n" "$root/artifacts/cef-supervisor/native/lumyte-cef-supervisor"
