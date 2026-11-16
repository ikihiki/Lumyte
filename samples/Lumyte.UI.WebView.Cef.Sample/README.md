# Experimental CEF desktop sample

This is an opt-in, unfinished integration sample, not a production browser. It uses CefGlue.Common 120.6099.215 and an old Chromium runtime. It is excluded from the ordinary solution and packaging; build it explicitly only when evaluating CEF.

```sh
dotnet build samples/Lumyte.UI.WebView.Cef.Sample -c Release
dotnet run --project samples/Lumyte.UI.WebView.Cef.Sample -c Release --no-build -- --enable-experimental-cef https://example.com/
```

The sample launcher also exposes ui-webview with the cef-experimental backend. Without explicit opt-in, the main process exits with code 2 before starting CEF. CEF subprocess arguments are handled before normal application startup.

Sandbox remains enabled. Initialization may terminate the process if the environment cannot provide a usable sandbox. Do not disable security policy or install privileged helpers automatically. The current Linux verification host fails sandbox initialization before OSR begins; a display server does not resolve that independent restriction. Windows startup is explicitly rejected because the native pre-CLR sandbox bootstrap is not implemented.

The adapter compiles real OSR, navigation/history, basic pointer/key/focus, GPU upload and ordered shutdown code. Real browser/GPU display remains unverified. IME, desktop native text acquisition, wheel/right-button input, interactive permission approval and GPU sharing are incomplete. Image reuse may wait for GC and resize callbacks lack a reliable native generation token. CEF NativeAOT and redistributable sandbox packaging are unverified.

Use samples/Lumyte.UI.WebView.Sample for the engine-free selfcheck. Its successful run does not verify this native browser path.
