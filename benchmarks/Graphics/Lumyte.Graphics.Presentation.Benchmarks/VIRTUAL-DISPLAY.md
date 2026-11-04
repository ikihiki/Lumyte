# Isolated virtual display validation

A physical login session is not required for **virtual presentation functional
checks**. The maintained Weston headless backend plus XWayland supplies an owned
X11 window/surface path. On the verified host, both compositor GL rendering and
client WebGPU rendering select the actual Radeon hardware; this is not Xvfb or a
software GPU replacement. No greeter display/authentication, physical seat, system
installation or persistent desktop configuration is used.

## Scope

- U03 mounted native-text/UI and actual RGBA-to-BGRA product-shader composition.
- Actual Acquire/Submit/Present calls, bounded slot completion and resource drain at
  depths1/3; exact/analytic preflight and final UI checks.
- Benchmark-owned stop/resume policy: no reservations during the controlled wait,
  then actual frames continue. This is not product demand-idle proof.
- Separate **real product host** probe uses public UseDesktop/UseContent, a custom
  OnRecord observer, Dispatcher.InvokeAsync and CurrentSurface.Rebuild. It records
  idle CPU callback deltas and controlled model-update-to-record delivery, then
  awaits normal Stopped state. GPU draw/Present/reservation counters and exact app
  adapter identity remain null when not exposed through the chosen public route.

The current product host renders periodically. Its idle recording continues;
zero-work demand-idle is **not implemented**, not a successful zero-reservation
result. See UiApplication.Frames.cs and recording-cache tests: reusable commands
are not equivalent to skipping GPU frame submissions. No production behavior or
API is changed to manufacture the desired result.

## ADR scope boundary

The output is synthetic, configured nominal60Hz. XRandR exposes a synthetic
modeline (recorded verbatim), not a real monitor measurement. FIFO/Present returns
under this compositor do not establish physical VSync, scanout, display-onset,
monitor refresh or input-to-photon latency. Virtual observations must never be
reported as those excluded measurements. Physical-monitor-dependent items are
outside ADR scope, not deferred tests, acceptance conditions or blockers. These
are virtual functional checks, not new formal performance campaigns.

## Reproduce without system installation

The tested Ubuntu host uses Weston14 packages. Download from its configured
distribution repository and extract into ignored artifacts; no sudo/install or
service/configuration change is required. Keep package versions/hashes with logs.

```sh
base="$PWD/artifacts/graphics-benchmarks/virtual-display"
mkdir -p "$base/packages" "$base/root"
(cd "$base/packages" && apt-get download weston libweston-14-0 libxcb-composite0)
for package in "$base"/packages/*.deb; do
    dpkg-deb -x "$package" "$base/root"
done
sha256sum "$base"/packages/*.deb > "$base/packages/package-sha256.txt"
source tools/setup/activate.sh
dotnet build benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -c Release
bash tools/benchmarks/graphics-scenarios/run-virtual-display.sh "$base/root" "$PWD/artifacts/graphics-benchmarks/virtual-proof-new"
python3 tools/benchmarks/graphics-scenarios/check-virtual-results.py --root artifacts/graphics-benchmarks/virtual-proof-new
```

Use a fresh output path. The runner expects the extracted Debian-style Weston14
layout; other platforms/versions require their own verified prefix. It creates a
private short /tmp runtime directory (Unix socket path limits), uses --no-config,
headless GL and a private D-Bus session, and lets Weston allocate its own DISPLAY.
It does not guess a display number or reuse another session. The compositor exits
when its owned client script finishes; timeouts and client/server exit codes remain
in the evidence. Any software-compositor fallback invalidates the hardware check.

run-virtual-display.sh serially exercises UI depth1/depth3, benchmark-owned idle and
the standard product-host observation. --display-kind virtual is recorded in
client output. Caller classification is corroborated by the isolated launch log,
actual compositor GPU and synthetic XRandR output; the flag alone is not proof.

Direct host-probe command within the owned virtual session:

```sh
dotnet run --no-build -c Release --project benchmarks/Graphics/Lumyte.Graphics.Presentation.Benchmarks -- --probe-product-host --idle-ms 1000 --display-kind virtual
```

## Verified local evidence

The four functional cases passed in ignored virtual-validated-001/. The three
presentation cases each contain120 actual frame observations; the separate product
host probe observes continued idle recording and the controlled revision update.
validation.json verifies identities, GPU selection, completion ordering, exact UI
checks and honest null/private counters. No test uses a performance threshold.
Earlier failed compositor startup probes remain preserved; the final owned
compositor/XWayland processes terminated after validation.
