# Browser-local Linux cleanup owner

This standalone, std-only executable has no CEF linkage, build script, crates.io dependencies, or Python runtime. Build with `bash tools/setup/build-cef-supervisor.sh` (Rust 1.99.1, locked/offline). The engine-free helper fixture alone imports its incremental MSBuild target; ordinary managed UI consumers do not.

```text
lumyte-cef-supervisor --owner UI_PID --root PRIVATE_ROOT --stop PRIVATE_ROOT/stop -- /absolute/helper --lumyte-helper [--lumyte-allow-unsandboxed]
```

The caller must be the expected parent. The absolute canonical, caller-owned, mode0700 `lumyte-browser-*` directory must fit 59 filesystem bytes. Parent pidfd identity checks, a subreaper, and a self pidfd signal-zero probe precede launch. Protocol fd0/1 pass directly to the helper; the supervisor closes its copies immediately and never reads/relays data. Marker, parent death, signals, or primary exit initiate bounded owned-child pidfd termination/reaping. Numeric PIDs are never signalled. Root removal occurs only after all owned descendants are reaped. Codes: child status or 128+signal; invalid invocation/root 2; cleanup/startup failure 125 (retain root); launch failure 127. Do not forcibly kill a cleanup owner on timeout.

Portal contract: prepend `--portal-session`; owner is the outer Bash launcher, command is that absolute launcher plus the same strict helper flags, and marker is `PRIVATE_ROOT/isolated-stop`. The supervisor writes the private bus configuration with percent-encoded UNIX address, starts owned `dbus-daemon --nofork --nopidfile --config-file=...` with fd0/8/9 closed, then `/bin/bash LAUNCHER --private-session ...` with the known private address, redirects service output to private-bus.log, inherits fd0/8/9 without a protocol relay, and closes its copies after spawn. It reaps its descendants and removes only its bus socket, NEVER the shared root; the outer supervisor owns root removal. Both waiting Bash owners close their protocol copies after spawn, and portal services close fd0/8/9. There is no dbus-run-session intermediary retaining fd9. No host user-manager environment, services, security controls or sandbox default is changed.

`cargo +1.99.1 test --locked --offline --release --manifest-path src/UI/Lumyte.UI.WebView.Cef.Supervisor/Cargo.toml --target-dir artifacts/cef-supervisor/cargo -- --test-threads=1` exercises real native process boundaries.
