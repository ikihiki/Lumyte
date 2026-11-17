# Lumyte.UI.WebView

The native UI is primary. WebView is an explicitly selected fallback and borrows a persistent, window-owned session. Rebuilding a declaration does not recreate the browser or navigate again.

The public Application.WebView.Cef integration uses a separate Rust/CEF156 process. The UI process does not load CefGlue or libcef. Missing helpers, native aborts, startup/navigation timeouts, malformed IPC and cancellation fault only the browser session; callers can replace the view with their ordinary UI. No implicit retries or automatic sandbox-disable fallback are provided. AllowUnsandboxed=true explicitly selects an unsafe mode for trusted content; its default is false and process isolation is not a security boundary without the sandbox.

## Current capability boundary

- Initial HTTP/S document display only; InputAvailable, PopupAvailable, ImeAvailable and NavigationCommandsAvailable are false. Post-creation navigation/history/reload/stop explicitly throw rather than succeeding without effect. Cancel stops the optional helper, not the UI application.
- The optional backend requires Linux, pidfd support and a separate CEF-free native supervisor beside the helper (or an explicit SupervisorExecutablePath). Production startup, teardown and the optional portal launcher have no Python dependency. Missing prerequisites fault only the browser; other platforms remain explicitly unavailable.
- .NET and the CEF helper communicate through directly inherited pipes. The supervisor never relays pixels or commands. A backend-private Lumyte.Execution session workflow owns asynchronous I/O and its joined lifetime; cancellation precedes runtime shutdown.
- One session, bounded commands and packets, latest-only CPU frames, owned child profile/temp and deadline-controlled process teardown. Existing scene/frame/upload resource lifetimes remain in effect.
- CPU pixel transport is implemented. The explicit unsandboxed product helper has passed a trusted loopback HTTP, exact-pixel, resize and clean-exit functional trial. Sandboxed browser rendering and actual UI GPU composition remain unqualified on this host; default sandbox failure stays local to the browser.
- Windows bootstrap, persistent profiles, interactive input, DOM automation, accessibility bridging, JS/host RPC and GPU sharing are unsupported.
- Old Image reachability may delay texture-slot reuse until GC; same-size resize revisits lack a native paint-generation fence.

Normal solution/build/pack does not fetch or launch CEF. Build and select the helper explicitly; an optional Linux session launcher can isolate DBus/portal setup to that child. No host desktop service, environment, AppArmor or setuid configuration is modified.

Use the engine-free sample and TestUiWebView for deterministic contracts. The fake helper fixture exercises real process failures without Chromium; a conditional real-helper test verifies that sandbox abort does not abort the UI owner. These tests are not evidence of accepted page rendering.
