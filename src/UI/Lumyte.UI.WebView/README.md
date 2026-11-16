# Lumyte.UI.WebView

Engine-independent browser placement and a persistent, window-owned session/controller. The declaration borrows its session; rebuilding it never initiates navigation or recreates browser history. Navigate, history, reload and stop are explicit commands. State is read-only and changes are reported on the UI owner.

This experimental implementation includes bounded command and CPU pixel queues, exclusive view attachment, popup placement, pointer/focus and key forwarding, and a committed-text input contract. The optional CEF/Application adapters contain real OSR callbacks and GPU upload/retirement code; fake tests are not evidence of real browser rendering. The ordinary solution and package distribution do not include the CEF runtime.

## Limits

- Real browser rendering and real GPU composition have not passed acceptance testing. Sandboxed CEF initialization is blocked in the current verification environment. No sandbox-disable fallback is provided.
- The available CefGlue package uses Chromium 120 and is not recommended for production. It is explicitly experimental and opt-in.
- Windows startup rejects execution until a native pre-CLR sandbox bootstrap is supplied. IME is unavailable on both platforms; RequireIme rejects configuration.
- Desktop native committed-text acquisition, wheel and right-button routing are not connected. The common input contract alone is not end-to-end text editing support.
- Native paint callbacks cannot fully distinguish an old frame after resizing back to the same dimensions.
- Conservative image reachability guards can stall image reuse until garbage collection. No forced GC is performed.
- GPU sharing, page DOM automation, accessibility bridging, JavaScript/host RPC and interactive permission approval are not implemented. Browser requests are denied by default.

Use the engine-free sample and TestUiWebView to verify deterministic session and resource contracts. Its NativeAOT result does not establish CEF NativeAOT compatibility.
