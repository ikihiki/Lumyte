# Native UI with an optional browser fallback

The default sample shows native UI and does not start a browser. The old in-process CefGlue120 adapter is removed. The optional backend is a separately built Rust/CEF156 process.

```sh
dotnet run --project samples/Lumyte.UI.WebView.Cef.Sample -c Release
bash tools/setup/build-cef-helper.sh
export LUMYTE_CEF_HELPER="$PWD/artifacts/cef-helper/target/debug/lumyte-cef-helper"
dotnet run --project samples/Lumyte.UI.WebView.Cef.Sample -c Release -- --enable-experimental-cef
```

Use an absolute helper path. If it is missing, crashes, hangs or cannot initialize sandbox, the session faults locally and the sample returns to a native button. Build, registration and normal UI startup do not initialize CEF. The backend has one read-only initial document, finite startup/navigation deadlines and explicit cancellation; later navigation, input, IME and popup capabilities are false.

The explicit build script stages an optional lumyte-cef-helper-isolated launcher beside the helper. Select that path instead only when the Linux session/portal workaround is needed; its dependencies and display must exist. It starts only an owned private bus/portal and does not modify the host user manager or desktop services. Missing setup remains a browser-local failure. See the native HELPER.md for the exact launcher contract.

Sandbox remains enabled by default. To explicitly evaluate trusted local content without it, add `--allow-unsandboxed-cef --browser-uri=http://127.0.0.1:PORT/pattern` together with `--enable-experimental-cef`. The URI is mandatory in unsafe mode; no external sample page is silently opened. This is not network isolation, and a separate process does not protect against malicious pages without the sandbox.

The product helper has passed a trusted unsandboxed loopback HTTP/pixel/resize/clean-exit trial. This host has not passed real sandboxed HTTP/paint/resize/close acceptance. The previous sandbox-OFF temporary diagnostic only qualified its isolated bus/portal environment, not this production helper or GPU composition. The native helper is explicitly built, excluded from ordinary solution packaging and not a supported arbitrary-site browser.
