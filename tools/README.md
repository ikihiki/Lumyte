# Tools

Build-time and offline tooling. Runtime projects must not reference these assemblies as runtime dependencies.

- [Native shader compiler](Lumyte.Graphics.Native.Shaders.Offline/README.md): Slang → DXIL／SPIR-V、target 別 host C# と管理入力 schema。
- [Portable shader compiler](Lumyte.Graphics.Portable.Shaders.Offline/README.md): 公式 Tint による WGSL reflection → package、host C# と binding 入力 schema。
- [MSBuild consumer](experiments/shader-build-inputs/README.md): shader から C# consumer までの実行可能な接続例。

`Lumyte.Graphics.Shaders.Offline.Shared` は出力 inventory を管理する CPU 処理の source link であり、共通 GPU ABI や runtime assembly ではない。
