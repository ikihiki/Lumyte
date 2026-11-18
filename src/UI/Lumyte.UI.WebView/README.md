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

## Input and JavaScript

Use the same window-owned session returned by UseWebView. Availability is
negotiated with the native helper: check InputAvailable, ScriptAvailable and
HostMessagesAvailable rather than assuming that registration enables them.
The matching helper must be rebuilt explicitly; legacy protocol helpers fail
closed instead of silently accepting unsupported commands.

Primary mouse, wheel, focus and physical-key transitions are forwarded through
the ordinary UI input route. Committed Unicode text is separate from physical
keys, so a key transition does not insert a second character. A committed batch
is bounded to 1024 UTF-16 units and 3072 UTF-8 bytes and is admitted atomically.
Touch, pen and secondary mouse buttons are not implemented. Full IME preedit,
composition/caret integration and OS IME acceptance are not established;
ImeAvailable remains false. Committed Japanese or emoji input alone is not an
IME acceptance test.

EvaluateScriptAsync(expression, timeout, cancellationToken) asynchronously waits
for a JSON-encoded result from a synchronous main-frame JavaScript expression.
It does not await JavaScript Promises. Scripts and results are bounded to 3072
UTF-8 bytes, with at most 16 pending requests, a default five-second deadline
and a maximum thirty-second deadline. Cancellation or timeout bounds waiting,
not hard interruption of JavaScript already executing in the isolated renderer.
An accepted script is never automatically replayed. Navigation or close aborts
pending requests; script exceptions and unsupported results are explicit errors.
Treat script error text and all page data as untrusted. JSON container nesting is
limited to 64 levels (the root container counts as one): an excessive script
result fails only its request; an excessive page message is rejected rather
than disabling the surviving document.

Page-to-.NET messaging requires explicit CefWebViewOptions.AllowedMessageOrigins.
The default empty list disables it. Use exact canonical HTTP(S) origins including
nondefault ports; wildcards, credentials, paths, query and fragments are rejected.
The page calls window.lumyte.postMessage(value); the session exposes an owner-thread
WebMessageReceived event with Source, Json and IsMainFrame. Only the current main
frame of an allowed origin may send. The native browser checks actual CEF frame
identity and address, and managed delivery checks the origin again. A payload's
own claimed source is not authority. An allowed origin is a web trust domain:
same-origin code can normally access the main document and cause it to execute
callbacks, so main-frame checks do not establish isolation from all same-origin
embedded scripts. This is bounded JSON messaging, not arbitrary .NET host-object
exposure, remote debugging or network isolation.

Do not add a session event subscription on every function evaluation. Bind
handlers once with an explicitly owned lifetime, or use the WebView declaration's
onWebMessageReceived callback so the retained element owns its subscription and
uses the latest callback after a rebuild.

## Current capability boundary

- HTTP/S document display with negotiated input and bounded main-frame JavaScript. PopupAvailable and ImeAvailable remain false. Explicit post-creation navigation/history/reload/stop commands remain unavailable and throw rather than succeeding without effect; user navigation is still subject to the native HTTP/S policy. Cancel stops the optional helper, not the UI application.
- The optional backend requires Linux, pidfd support and a separate CEF-free native supervisor beside the helper (or an explicit SupervisorExecutablePath). Production startup, teardown and the optional portal launcher have no Python dependency. Missing prerequisites fault only the browser; other platforms remain explicitly unavailable.
- .NET and the CEF helper communicate commands/state/tiny frame notices through directly inherited pipes; BGRA pixels use a budgeted fixed three-slot shared mapping. Reading slots cannot be overwritten, and stale Ready frames may be replaced. The supervisor never relays pixels or commands. A backend-private Lumyte.Execution session workflow owns asynchronous I/O and its joined lifetime; cancellation precedes runtime shutdown.
- One session, bounded commands and packets, latest-only CPU frames, owned child profile/temp and deadline-controlled process teardown. Existing scene/frame/upload resource lifetimes remain in effect.
- GPU staging explicitly awaits Upload-buffer mapping before CPU conversion/copy, then unmaps before recording and submitting buffer-to-texture work. Mapping and pre-submit failures release their source/staging/destination leases; ambiguous submitted work still requires retirement before destruction.
- Shared-memory CPU pixel transport retains two copies: CEF callback to a shared slot, then the leased slot to the existing owned mailbox. It is not GPU sharing or zero-copy. The explicit unsandboxed product helper has passed a trusted loopback HTTP, exact-pixel, resize and clean-exit functional trial. A trusted unsandboxed loopback page has also been captured alongside native controls and text through the actual Desktop Drawing2D/presenter/swapchain path using software Vulkan. This establishes initial composition, not hardware performance, arbitrary-site support or sandboxed qualification. Default sandbox failure stays local to the browser.
- Windows bootstrap, persistent profiles, OS IME, general DOM automation, accessibility bridging, arbitrary JS/host-object RPC and GPU sharing are unsupported. Programmatic expression evaluation and origin-restricted JSON messages do not enable these capabilities.
- Old Image reachability may delay texture-slot reuse until GC; same-size resize revisits lack a native paint-generation fence.

Normal solution/build/pack does not fetch or launch CEF. Build and select the helper explicitly; an optional Linux session launcher can isolate DBus/portal setup to that child. No host desktop service, environment, AppArmor or setuid configuration is modified.

Use the engine-free sample and TestUiWebView for deterministic contracts. The fake helper fixture exercises real process failures without Chromium; a conditional real-helper test verifies that sandbox abort does not abort the UI owner. These tests are not evidence of accepted page rendering.
