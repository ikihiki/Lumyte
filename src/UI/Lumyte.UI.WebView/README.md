# Lumyte.UI.WebView

The native UI is primary. WebView is an explicitly selected fallback and borrows a persistent, window-owned session. Rebuilding a declaration does not recreate the browser or navigate again.

The public Application.WebView.Cef integration uses a separate Rust/CEF156 process. The UI process does not load CefGlue or libcef. Missing helpers, native aborts, startup/navigation timeouts, malformed IPC and cancellation fault only the browser session; callers can replace the view with their ordinary UI. No implicit retries or sandbox-disable fallback are provided.

## Current capability boundary

- Initial HTTP/S document display only; InputAvailable, PopupAvailable, ImeAvailable and NavigationCommandsAvailable are false. Post-creation navigation/history/reload/stop explicitly throw rather than succeeding without effect. Cancel stops the optional helper, not the UI application.
- One session, bounded commands and packets, latest-only CPU frames, owned child profile/temp and deadline-controlled process teardown. Existing scene/frame/upload resource lifetimes remain in effect.
- CPU pixel transport is implemented; actual sandboxed browser rendering, GPU composition and resizing are not acceptance-qualified on this host. Sandbox startup currently aborts in the child and the UI continues.
- Windows bootstrap, persistent profiles, interactive input, DOM automation, accessibility bridging, JS/host RPC and GPU sharing are unsupported.
- Old Image reachability may delay texture-slot reuse until GC; same-size resize revisits lack a native paint-generation fence.

Normal solution/build/pack does not fetch or launch CEF. Build and select the helper explicitly; an optional Linux session launcher can isolate DBus/portal setup to that child. No host desktop service, environment, AppArmor or setuid configuration is modified.

Use the engine-free sample and TestUiWebView for deterministic contracts. The fake helper fixture exercises real process failures without Chromium; a conditional real-helper test verifies that sandbox abort does not abort the UI owner. These tests are not evidence of accepted page rendering.
