# CEF sandbox selection probe

This is an acceptance probe, **not a browser sample**. It uses the actual CefGlue.Common 120.6099.215 package and its CEF 120.1.8 native dependency. No fake driver is used. It requests sandboxed initialization and never retries without sandbox. This old Chromium version is not approved for production.

```sh
dotnet build samples/Lumyte.UI.WebView.Cef.Probe -c Release
DOTNET_ROOT=/path/to/pinned-dotnet \
LD_LIBRARY_PATH="$PWD/samples/Lumyte.UI.WebView.Cef.Probe/bin/Release/net10.0/CefGlueBrowserProcess" \
samples/Lumyte.UI.WebView.Cef.Probe/bin/Release/net10.0/Lumyte.UI.WebView.Cef.Probe
```

Run in a separate process: Chromium can abort on initialization failure. On this Linux host it aborts with `No usable sandbox!` (exit 133), before OSR creation. The native package contains no chrome-sandbox helper. This host also has no DISPLAY or Xvfb; a display backend is a separate gate. Do not disable AppArmor, change namespace policy, or install a setuid helper automatically.

OutSystems/CefGlue source at `4fdf9cee5c2a9c49708abe27a774a585e1c7e388` matches the package repository SHA. Its Common/CefRuntimeLoader forces NoSandbox=true on Linux. This probe uses the low-level managed binding instead. OSR, close and IME callback availability does not prove Lumyte texture/input/lifetime integration. NativeAOT, trimming, Windows native sandbox bootstrap and IMM32 remain unverified.

Before redistribution verify helpers/permissions, data/locales, licenses and third-party notices, ABI/security updates and supported RIDs. This package brings other OS runtime assets transitively; production packaging must avoid unnecessary payload.
