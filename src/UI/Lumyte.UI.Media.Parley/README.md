# Lumyte.UI.Media.Parley

Display-only text adapter for a caller-owned `ParleyTextEngine2D`. Register fonts explicitly before layout; no OS font discovery or synchronous file loading is performed by this adapter. It does not depend on TextDocument or TextBox.

Create a `ParleyTextLayoutProvider(engine, options)` and supply it explicitly to `Lumyte.UI.Media.Compose.Text(value: "Hello", layoutProvider: provider, width: 160)`. The provider copies every options list at construction and overrides wrapping width on each synchronous call. Zero width produces an empty snapshot without invoking the engine.

Browser source consumers use the existing `build/Targets/ParleyBrowser.targets` in their application project and pass `LumyteBuildParleyNative=false;LumyteParleyBrowser=true` to the adapter reference, matching the existing Graphics.Text.Parley Browser sample. The adapter does not add another native ABI or browser loader. NativeAOT uses the ordinary native Parley asset.

Engine and font registration remain borrowed. Completed vector snapshots survive engine disposal. Any external GPU paint resources retain their existing caller-owned frame-completion lifetime. Managed and native Parley binaries must use the same ABI/package version.
