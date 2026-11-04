#!/usr/bin/env bash
# Synthetic output only: no greeter, physical seat, system install or persistent config.
set -euo pipefail
repo=$(git -C "$(dirname -- "$0")" rev-parse --show-toplevel)
prefix=${1:-"$repo/artifacts/graphics-benchmarks/virtual-display/root"}
output=${2:-"$repo/artifacts/graphics-benchmarks/virtual-display-run-$(date +%Y%m%d-%H%M%S)"}
prefix=$(realpath "$prefix")
test -x "$prefix/usr/bin/weston" || { echo "Extract/install a compatible Weston prefix first." >&2; exit 2; }
test ! -e "$output" || { echo "Use a fresh output directory." >&2; exit 2; }
mkdir -p "$output"
output=$(realpath "$output")
runtime=$(mktemp -d /tmp/lumyte-vd.XXXXXXXX)
trap 'rmdir "$runtime" 2>/dev/null || true' EXIT
export XDG_RUNTIME_DIR="$runtime"
export LD_LIBRARY_PATH="$prefix/usr/lib/x86_64-linux-gnu:$prefix/usr/lib/x86_64-linux-gnu/weston:$prefix/usr/lib/x86_64-linux-gnu/libweston-14${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export WESTON_MODULE_MAP="$(find "$prefix/usr/lib" -type f -name '*.so' -printf '%f=%p;')"
export WESTON_DATA_DIR="$prefix/usr/share/weston"
export LUMYTE_VIRTUAL_OUTPUT="$output"
export LUMYTE_PRESENTATION_DLL="$repo/benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks/bin/Release/net10.0/Lumyte.Graphics.Presentation.Benchmarks.dll"
test -f "$LUMYTE_PRESENTATION_DLL" || { echo "Build the Release presentation project first." >&2; exit 2; }
export VK_DRIVER_FILES=/usr/share/vulkan/icd.d/radeon_icd.json
printf '%s\n' "$runtime" > "$output/runtime-path.txt"
printf '%s\n' virtual-weston-headless-gl-xwayland > "$output/display-kind.txt"
"$prefix/usr/bin/weston" --version > "$output/weston-version.txt"
sha256sum "$prefix/usr/bin/weston" "$prefix/usr/lib/x86_64-linux-gnu/libweston-14/headless-backend.so" "$prefix/usr/lib/x86_64-linux-gnu/libweston-14/gl-renderer.so" "$prefix/usr/lib/x86_64-linux-gnu/libweston-14/xwayland.so" > "$output/runtime-files-sha256.txt"
if test -f "$prefix/../packages/package-sha256.txt"; then
    cp "$prefix/../packages/package-sha256.txt" "$output/packages-sha256.txt"
fi
cat > "$output/client.sh" <<'CLIENT'
#!/usr/bin/env bash
set -u
output="$LUMYTE_VIRTUAL_OUTPUT"
printf 'DISPLAY=%s\nWAYLAND_DISPLAY=%s\n' "$DISPLAY" "$WAYLAND_DISPLAY" > "$output/allocated-display.txt"
xrandr --current > "$output/xrandr-virtual-modes.txt" 2>&1
failure=0
run_case() {
    local name=$1
    shift
    timeout --signal=TERM 60s dotnet "$LUMYTE_PRESENTATION_DLL" "$@" --display-kind virtual > "$output/$name.jsonl" 2> "$output/$name.stderr"
    local code=$?
    printf '%s exit=%s\n' "$name" "$code" | tee -a "$output/client-status.txt"
    test "$code" -eq 0 || failure=1
}
run_case u03-depth1 --run --scenario u03-forced-draw --depth 1 --frames 120 --warmup 8 --present-mode fifo --refresh-hz 60
run_case u03-depth3 --run --scenario u03-forced-draw --depth 3 --frames 120 --warmup 8 --present-mode fifo --refresh-hz 60
run_case owned-idle --run --scenario benchmark-idle-u03 --depth 1 --frames 120 --warmup 8 --present-mode fifo --refresh-hz 60
run_case product-host --probe-product-host --idle-ms 1000
printf '%s\n' "$failure" > "$output/client-exit.txt"
exit "$failure"
CLIENT
set +e
timeout --signal=TERM 260s env -u DISPLAY -u WAYLAND_DISPLAY -u XAUTHORITY dbus-run-session -- "$prefix/usr/bin/weston" --backend=headless --renderer=gl --shell=kiosk-shell.so --xwayland --no-config --width=1920 --height=1080 --scale=1 --refresh-rate=60000 --socket=wayland-lumyte --log="$output/weston.log" -- /bin/bash "$output/client.sh" > "$output/compositor.stdout" 2> "$output/compositor.stderr"
server_code=$?
set -e
printf '%s\n' "$server_code" > "$output/compositor-exit.txt"
test "$server_code" -eq 0
test -f "$output/client-exit.txt" && test "$(cat "$output/client-exit.txt")" -eq 0
grep -q 'Using rendering device: /dev/dri/render' "$output/weston.log"
grep -q 'GL renderer:' "$output/weston.log"
! grep -Ei 'GL renderer:.*(llvmpipe|softpipe|software)' "$output/weston.log"
cat "$output/client-status.txt"
echo "Virtual compositor checks completed; synthetic output is NOT physical scanout/VSync proof."
