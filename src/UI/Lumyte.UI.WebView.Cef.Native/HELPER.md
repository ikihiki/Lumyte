# Optional CEF156 process helper

This is a separate opt-in executable, not the fixed-size selection-probe ABI in
lib.rs. The ordinary solution/build/pack does not restore/build this helper.
Linux x64 is the compiled target. CEF156 sandbox remains enabled. This host
currently aborts in the native sandbox bootstrap: **no sandboxed browser, Ready,
HTTP load, real frame, resize or asynchronous-close acceptance is claimed**.
The previous private-bus qualification was explicitly sandbox-OFF diagnostic
only and does not qualify this helper.

## Build and stage

From repository root: `bash tools/setup/build-cef-helper.sh`. Uses cached Rust
1.99.1 and the existing pinned CEF cache; set CEF_PATH if its location differs.
Output: `artifacts/cef-helper/target/debug/lumyte-cef-helper`, beside CEF libraries
and resources staged by cef-rs. Keep that directory together. The executable has
Linux $ORIGIN RUNPATH; launch with executable-directory cwd, sole managed argument
`--lumyte-helper`. CEF re-launches this same executable with --type children,
which bootstrap before any stdin reader or protocol writer is created.

## Managed supervisor requirements

The optional managed adapter currently requires Linux, /usr/bin/python3 and
pidfd support. Its embedded supervisor is a separate process and uses subreaper
adoption plus stable pidfd signaling to reclaim descendants after helper exit
or abort, including nested session launchers. Missing prerequisites fault only
the browser. Normal UI use does not require Python or native CEF.

## Protocol and lifecycle

One session (1). u32LE packet size excludes its 4-byte prefix; maximum 16,777,280.
40-byte payload header: u32 kind, u64 session, i64 generation, i64 sequence,
i32 width, i32 height, u32 body length. Bodies are bytes, URLs/reasons UTF-8.
Commands 1 start, 2 navigate, 3 viewport, 4 close, 5 back, 6 forward, 7 reload,
8 stop. Events 101 native-created Ready, 102 main-frame load-end observed URL,
103 copied premultiplied packed top-down BGRA8, 104 short fault, 105 closed after
shutdown. Loaded is not a paint or successful HTTP-status guarantee.

Input bodies are at most4096 bytes; non-URL commands require empty bodies.
Input generation and sequence must be nonnegative. Viewport generations must
strictly increase; positive dimensions <=4096 and packed bytes <=packet limit.
Old-size paints are discarded; accepted frames carry current owner-thread
geometry/generation and monotonically increasing event sequence. The managed fallback exposes initial navigation only; subsequent navigation/history/reload/stop commands are explicitly unavailable until command/load correlation is qualified. CEF does not
provide a paint generation token: equal-size revisits cannot distinguish the
age of compositor content, and this is not a presentation fence.

Parser allocation, input queue (8) and output queue (4) are bounded. Queue
saturation fails closed rather than blocking the native owner. Stdout's protocol
fd is duplicated CLOEXEC; normal fd1 and CEF children point to stderr. Native
callbacks and all browser operations run on the owner pump. EOF requests close;
creation deadline15s, close grace5s, final writer drain1s. CEF initialize/pump/
shutdown are native synchronous calls and cannot themselves be preempted:
the managed owner must enforce its process deadline/tree cleanup. The managed host supplies a private HOME/TMPDIR/XDG root and removes it after child exit, including native abort. A fatal CEF
bootstrap cannot emit a fault; process exit/EOF is the authoritative failure.

## Policy and remaining work

Popups/downloads/file dialogs/JS dialogs/media and permission prompts are denied;
before-unload is accepted for close. No input injection, clipboard, devtools,
custom-scheme, profile, accelerated OSR or Windows bootstrap capability. Initial
and explicit navigations require HTTP/S; navigation callback rejects other
schemes. This is **not network isolation**, a URL authority/security policy,
subresource filtering, complete browser hardening, or proof for arbitrary
untrusted content. Audio is muted. Per-process private temporary root plus
incognito cache; clean shutdown removes the root, fatal termination may leave it.
No host session environment, services, sysctl, AppArmor or setuid changes.
The optional private bus/portal launcher below is an ad-hoc startup mitigation,
not a sandbox workaround or a desktop-session repair.

Verification logs: artifacts/cef-helper/build.log, test.log, clippy.log,
protocol-smoke.json and sandbox-smoke.json / sandbox-smoke.stderr.log.
Protocol smoke covers EOF, close-before-start, unsupported capability, invalid
URL and malformed lengths without initializing CEF. Unit tests also validate
wire round-trip and geometry bounds. These are not rendered-browser tests.

## Optional Linux private-portal launcher (ad-hoc fallback)

UI remains primary; Browser remains optional. The build script also stages
$CARGO_TARGET_DIR/debug/lumyte-cef-helper-isolated beside the real
lumyte-cef-helper ELF and CEF resources. Only when this local workaround is
needed, set the existing HelperExecutablePath to that wrapper's absolute path
instead of the ELF. No new command-array/options API or automatic selection is
introduced. Keep the staged directory together; arguments remain exactly
--lumyte-helper and are passed without shell evaluation. CEF --type children
use the real ELF via current_exe(), not the wrapper or another private bus.

Requirements: Linux, Bash, Python3 (Linux subreaper support), dbus-run-session,
gdbus, coreutils timeout/readlink/stat/mkdir, and the distro frontend and GTK
backend at /usr/libexec/xdg-desktop-portal{,-gtk}, plus
/usr/libexec/xdg-permission-store. A working DISPLAY or
WAYLAND_DISPLAY must already exist. Relative Wayland sockets are resolved
against the inherited runtime directory before installing the private runtime.
The launcher never creates a compositor. Missing display/dependencies fail
nonzero with a short stderr reason, leaving the managed Browser Faulted while
UI can continue. All portal-name checks share a five-second startup budget
with 0.2-second per-query deadlines and bounded retries (process scheduling
aside). Backend/store names are acquired before starting the frontend.

The managed supervisor supplies a mode0700 lumyte-browser-* root as
HOME/TMPDIR/XDG_CONFIG_HOME/XDG_CACHE_HOME/XDG_DATA_HOME; the launcher rejects
other profile layouts. A private runtime/state directory, bus socket/configuration and GTK
preference file live only under that root. Overlong Unix socket paths fail
closed rather than falling back to a socket elsewhere. Frontend and GTK start
on a new owned D-Bus after the display is available, with LD_LIBRARY_PATH removed only for distro portal processes.
Readiness checks NameHasOwner on the explicitly private address, never the
host user bus. Portal and bus/activation logs go to private root files, not
protocol stdout; the native helper keeps its own stderr. Reserved protocol and
diagnostic descriptors are closed before entering the real ELF.

The private bus has no activation service directories: only the explicitly
started frontend, GTK and permission store exist. Its private desktop-specific
configuration prefers GTK. This deliberately narrower derivative of B avoids
auto-activated Documents/GVfs FUSE mounts and ancillary desktop daemons, whose
mounts cannot be cleaned by a process killed with SIGKILL. File/document portals,
accessibility, secret-store integration and other desktop integrations are
not qualified or promised by this fallback. GIO_USE_VFS=local and NO_AT_BRIDGE=1
apply only to directly started portal processes, not the native helper.
This is not a byte-for-byte replay of the earlier full-session B condition;
its engine-free readiness/lifecycle evidence does not establish a real-CEF
latency improvement or rendered-browser success.
The ELF inherits stdin/stdout unchanged. A non-exec supervisor reaps its own
process group on EOF-driven helper exit, native abort, startup failure or TERM;
the managed deadline still owns hung-native termination via
Kill(entireProcessTree:true). No global portal kill, host environment import,
user-manager restart, AppArmor/sysctl/setuid edit or sandbox-disable flag.
The parent removes the private root after exit; its files are not a persistent
user browser profile.

The adoption rationale is the earlier whole-session B comparison and native
Rust10/10 sandbox-OFF diagnostic qualification in
artifacts/cef-portal-{delay,qualification}/REPORT.md. Those results are **not**
sandboxed helper rendering acceptance. The launcher preserves the helper's
sandbox default; this host's native sandbox abort remains an expected failure,
not a reason to disable sandbox. Real browser Ready/HTTP/paint success and
physical-desktop/distribution coverage remain unqualified.

An engine-free smoke suite builds an ELF fixture in the sibling native-helper
position, uses an explicitly owned Weston (never the host display), checks
binary stdout/argument forwarding, EOF/abort/TERM, actual .NET process-tree
kill, fast failure and zero marked-process/private-bus residues. It is not part
of ordinary solution build/pack and never downloads its compositor:

    python3 tools/setup/smoke/cef-helper-launcher.py \
      --weston-root /absolute/path/to/extracted-weston-root \
      --out artifacts/cef-helper-launcher-smoke-UNIQUE

Optionally add --native-helper /absolute/path/to/lumyte-cef-helper to assert
this host-specific sandbox failure (exit133/SIGTRAP and the native SUID sandbox
error), without altering sandbox settings. This is a failure-path test only.

Each output directory must be new. Results and read-only host before/after
snapshots are retained there. GCC, Python3, a local .NET10 SDK and extracted
Weston libraries are test-only dependencies; the standalone test project uses
the installed SDK without changing the repository's pinned SDK or build files.
