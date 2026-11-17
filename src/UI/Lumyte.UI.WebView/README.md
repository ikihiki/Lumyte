# Lumyte.UI.WebView

The native UI is primary. WebView is an explicitly selected fallback and borrows a persistent, window-owned session. Rebuilding a declaration does not recreate the browser or navigate again.

The public Application.WebView.Cef integration uses a separate Rust/CEF156 process. The UI process does not load CefGlue or libcef. Missing helpers, native aborts, startup/navigation timeouts, malformed IPC and cancellation fault only the browser session; callers can replace the view with their ordinary UI. No implicit retries or automatic sandbox-disable fallback are provided. AllowUnsandboxed=true explicitly selects an unsafe mode for trusted content; its default is false and process isolation is not a security boundary without the sandbox.

## Function components without window/session parameters

Configure UseCefWebViews once, then call UiWebViewHooks.UseWebView(key, initialUri)
inside a generated composable function. The application automatically provides a
stable window-scoped session source; component arguments need neither a window nor
a browser session. Existing GetWebViewSession remains available for explicit use.

Inside function evaluation:

```csharp
using static Lumyte.UI.WebView.UiWebViewHooks;
using static Lumyte.UI.WebView.Compose;

var browser = UseWebView("help", new Uri("https://example.com/"));
return WebView(session: browser, width: 640, height: 360);
```

Use the generated function factory in the parent declaration, not a direct call
to its definition. Hooks require an active function build; callbacks capture the
returned session rather than invoking hooks again. Standalone surfaces can use
ProvideWebViewSessions with an explicitly borrowed IUiWebViewSessions provider.

A key identifies a window-owned session, not an element. The same key and initial
URI reuse it through signal updates, rebuilds and remounts. Changing the initial
URI for a key is rejected. One session may attach to only one live WebView, and
the CEF backend's single-session budget is unchanged. Unmounting a component does
not dispose the borrowed session; the owning window controls its teardown.
StateChanged is an ordinary event, not an implicit reactive hook: do not add a
subscription on every function evaluation.

The [mixed UI sample](../../../samples/Lumyte.UI.WebView.Cef.Sample/BrowserSampleUi.cs)
places a nested function's WebView inside a clipped, padded Panel alongside native
header and counter controls. Only document parameters cross component boundaries.

## Current capability boundary

- Initial HTTP/S document display only; InputAvailable, PopupAvailable, ImeAvailable and NavigationCommandsAvailable are false. Post-creation navigation/history/reload/stop explicitly throw rather than succeeding without effect. Cancel stops the optional helper, not the UI application.
- The optional backend requires Linux, pidfd support and a separate CEF-free native supervisor beside the helper (or an explicit SupervisorExecutablePath). Production startup, teardown and the optional portal launcher have no Python dependency. Missing prerequisites fault only the browser; other platforms remain explicitly unavailable.
- .NET and the CEF helper communicate commands/state/tiny frame notices through directly inherited pipes; BGRA pixels use a budgeted fixed three-slot shared mapping. Reading slots cannot be overwritten, and stale Ready frames may be replaced. The supervisor never relays pixels or commands. A backend-private Lumyte.Execution session workflow owns asynchronous I/O and its joined lifetime; cancellation precedes runtime shutdown.
- One session, bounded commands and packets, latest-only CPU frames, owned child profile/temp and deadline-controlled process teardown. Existing scene/frame/upload resource lifetimes remain in effect.
- Shared-memory CPU pixel transport retains two copies: CEF callback to a shared slot, then the leased slot to the existing owned mailbox. It is not GPU sharing or zero-copy. The explicit unsandboxed product helper has passed a trusted loopback HTTP, exact-pixel, resize and clean-exit functional trial. Sandboxed browser rendering and actual UI GPU composition remain unqualified on this host; default sandbox failure stays local to the browser.
- Windows bootstrap, persistent profiles, interactive input, DOM automation, accessibility bridging, JS/host RPC and GPU sharing are unsupported.
- Old Image reachability may delay texture-slot reuse until GC; same-size resize revisits lack a native paint-generation fence.

Normal solution/build/pack does not fetch or launch CEF. Build and select the helper explicitly; an optional Linux session launcher can isolate DBus/portal setup to that child. No host desktop service, environment, AppArmor or setuid configuration is modified.

Use the engine-free sample and TestUiWebView for deterministic contracts. The fake helper fixture exercises real process failures without Chromium; a conditional real-helper test verifies that sandbox abort does not abort the UI owner. These tests are not evidence of accepted page rendering.
