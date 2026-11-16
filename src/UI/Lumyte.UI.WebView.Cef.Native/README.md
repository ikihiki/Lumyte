# Rust CEF selection probe (not a product adapter)

This opt-in experiment proves a released cef-rs cdylib and a .NET
LibraryImport/UnmanagedCallersOnly boundary, including trimmed NativeAOT.
It deliberately leaves the existing CefGlue adapter unchanged: the available
Linux host aborts in sandbox initialization, before browser creation or OSR.
Compiled OSR/async-close code is **not** runtime acceptance evidence.

The exact release, upstream commit, binary identity and archive digests are
in `abi.json` and `Cargo.lock`. Sandbox/resources are enabled; accelerated OSR
is disabled. Ordinary solution/build/pack never restores or downloads CEF.
Explicit Cargo builds download approximately 333 MB and extract a large runtime.
Use an ignored target directory; do not add it to source control.

From repository root (use repository-private RustToolchain setup if necessary):

```sh
export CARGO_TARGET_DIR="$PWD/artifacts/cef-bridge/target"
cargo +1.99.1 build --locked --manifest-path src/UI/Lumyte.UI.WebView.Cef.Native/Cargo.toml
export LD_LIBRARY_PATH="$CARGO_TARGET_DIR/debug${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
cargo +1.99.1 test --locked --manifest-path src/UI/Lumyte.UI.WebView.Cef.Native/Cargo.toml
cargo +1.99.1 clippy --locked --manifest-path src/UI/Lumyte.UI.WebView.Cef.Native/Cargo.toml --all-targets -- -D warnings
cargo +1.99.1 fmt --check --manifest-path src/UI/Lumyte.UI.WebView.Cef.Native/Cargo.toml
dotnet run --project samples/Lumyte.UI.WebView.Cef.Native.Probe -c Release
dotnet publish samples/Lumyte.UI.WebView.Cef.Native.Probe -c Release -r linux-x64 -p:PublishAot=true -p:PublishTrimmed=true -o artifacts/cef-bridge/aot
artifacts/cef-bridge/aot/Lumyte.UI.WebView.Cef.Native.Probe
```

The default run loads the real Rust library and its real libcef dependency but
does not initialize CEF. It verifies ABI, null/version rejection, an actual
native-to-managed callback, copied synthetic BGRA bytes and no-runtime shutdown.
The BGRA callback is synthetic, **not** an observed browser frame. Without the
native library the probe exits 10 with `cef-native-missing`; ABI mismatch exits 3.

For a separately controlled, non-sensitive local HTTP acceptance page, add
`--browser http://127.0.0.1:PORT/`. This requests sandboxed initialization, one
64x64 OSR browser, paint, asynchronous close and shutdown, with a 15-second
pump deadline. Fatal sandbox initialization can terminate the process. Do not
change AppArmor/namespaces/setuid permissions or add no-sandbox to run it.
The operator must supply a trusted local URL and trusted page content. The
initial URL prefix check is not a parsed-authority check and does not create a
loopback-only security boundary. Redirects, navigation and subresource requests
are not restricted to loopback. There is no production network or permission
policy: do not serve untrusted content or expose this as a general browser.

## Remaining acceptance gates

The bridge has no product session handles, navigation queue, geometry generation
protocol, permission policy, profile integration, resize/popup/input or GPU path.
It is not connected to UseCefWebViews and does not remove CefGlue.Common.
Windows is explicitly rejected: official sandbox bootstrap plus CLR-hosting
client integration, Windows compile/bundle and Windows execution are unverified.
The host has no desktop; a compositor cannot fix its independent sandbox failure.
Product migration must wait for actual sandboxed OSR/close acceptance and a
complete reviewed adapter/ABI preserving the existing C# contracts.
