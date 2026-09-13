# Shader build → 管理入力の確認

実 compiler、MSBuild targets、Resources generator、生成 C# consumer を接続する小さな console project。
入力は shader と build 設定 JSON だけで、XML、offset、binding 番号の別定義は持たない。
Native と Portable は異なる順序で targets を import し、どちらも生成入力を CoreCompile に渡す。
GPU は使用しない。実 compiler を起動する隣接 xUnit conformance とあわせて用いる。

repository root から、Slang 2026.17 と対応する DXC、公式 Dawn `v20260911.162847` の `tint_info`／`tint` を指定する。
引数のパスは使用環境に合わせる。

```powershell
dotnet build tools/experiments/shader-build-inputs/Native/Native.csproj -p:LumyteNativeSlangCompiler="C:/tools/slang/bin/slangc.exe" -p:LumyteNativeDownstreamCompilerDirectory="C:/tools/dxc/bin/x64"
dotnet run --project tools/experiments/shader-build-inputs/Native/Native.csproj --no-build
dotnet build tools/experiments/shader-build-inputs/Portable/Portable.csproj -p:LumytePortableTintInfo="C:/tools/dawn/bin/tint_info.exe"
dotnet run --project tools/experiments/shader-build-inputs/Portable/Portable.csproj --no-build
```

生成先は各 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native|Portable/`。
同じ入力でも DX12 の root は32 byte、Vulkan は48 byteとなる。Portable の別 WGSL root は32 byte。
各 consumer は生成 host 型の size と、管理入力型の `AbiHash` が同時に生成された package に一致することを確認する。
Native の Parameter Data は shader の型指定から反映されるが、値の作成・転送は caller が行う。
