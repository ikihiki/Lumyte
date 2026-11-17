#!/usr/bin/env bash
# Explicit opt-in only. Never invoked by solution build/pack. No sandbox changes.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export CARGO_HOME="${CARGO_HOME:-$root/artifacts/build/tools/cargo}"
export RUSTUP_HOME="${RUSTUP_HOME:-$root/artifacts/build/tools/rustup}"
export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$root/artifacts/cef-helper/target}"
export PATH="$CARGO_HOME/bin:$PATH"
# Reuse the pinned, validated cache when available; CEF_PATH can select another
# matching cache. Cargo offline alone does not make CEF build.rs offline.
if [[ -z "${CEF_PATH:-}" ]]; then
  cache="$root/artifacts/cef-bridge/target/debug/build/cef-dll-sys-7b5cf1348f794058/out/cef_linux_x86_64"
  if [[ ! -f "$cache/archive.json" ]]; then
    echo 'Set CEF_PATH to the extracted CEF156 cache (archive.json required).' >&2
    exit 2
  fi
  export CEF_PATH="$cache"
fi
cargo +1.99.1 build --offline --locked --manifest-path "$root/src/UI/Lumyte.UI.WebView.Cef.Native/Cargo.toml" --bin lumyte-cef-helper
bash "$root/tools/setup/build-cef-supervisor.sh" >&2
install -m755 "$root/artifacts/cef-supervisor/native/lumyte-cef-supervisor" "$CARGO_TARGET_DIR/debug/lumyte-cef-supervisor"
# cef resources feature stages runtime resources beside the executable.
# Optional wrapper; direct ELF remains the default.
install -m 755 "$root/tools/setup/run-cef-helper-isolated.sh" "$CARGO_TARGET_DIR/debug/lumyte-cef-helper-isolated"
printf '%s
' "$CARGO_TARGET_DIR/debug/lumyte-cef-helper"
