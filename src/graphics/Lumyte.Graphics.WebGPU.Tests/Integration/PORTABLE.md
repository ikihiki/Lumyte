# Portable WebGPU foundation tests

Windows の実 Dawn device を使う最初の Portable 適合試験。公開 `IPortableGpuBackend` と新しい `WebGpuBackend.CreateAsync` を使用する。旧 `IGpuBackend` の描画試験は `Legacy.WebGpuBackend` に残し、Portable の動作を代替しない。

```powershell
dotnet test src/graphics/Lumyte.Graphics.WebGPU.Tests/Lumyte.Graphics.WebGPU.Tests.csproj --filter "FullyQualifiedName~WebGpuPortable|FullyQualifiedName~WebGpuDiagnosticsTests|FullyQualifiedName~WebGpuLimitMappingTests"
```

`WebGpuPortableConformance` の実 GPU 試験は adapter/device、直接 root 入力の有効 limit、resource の実体生成、native `mapAsync` / mapped range / unmap を確認する。8 byte と 16 byte の要求を別 device で確認し、固定 64 byte ABI を前提にしない。オプション機能を要求する試験には dual-source blend と indirect-first-instance を実装した adapter が必要で、機能不足を成功や silent skip に置き換えない。

要求 limit は必要な最小容量であり、native device の有効値はより大きくなり得る。この PC の Dawn では 8 / 16 byte の要求に対し有効値がどちらも 64 byte だったため、実 GPU 試験は要求を満たすことを確認する。別の純粋な interop 試験で要求値そのものが 8 / 16 byte のまま native descriptor に渡ることを確認し、wrapper の固定 ABI と runtime の有効 limit を区別する。

buffer は明示した MapRead / MapWrite usage で作成する。非 zero offset、再 map 後の内容、read mapping の write 拒否、unmap 後に保存済み `Memory` / `ReadOnlyMemory` から span や pin を取得できないことを確認する。取得済み span / pointer 自体は失効させられないため、caller が unmap 前にその利用を終了する。

texture は 1D、2D array、3D、mutable format の生成を runtime 診断で確認する。無効な usage と texture size も runtime に渡して、その生成診断が別 object に混ざらないことを並行呼出しを含めて確認する。生成診断の取得は test-only の内部観測点であり、公開能力照会 API ではない。

`Diagnostics/WebGpuDiagnosticsTests` は GPU を使わず、生成診断と mapping 診断の到着順、両方の確定前に失敗を返さないこと、共有 object の診断再利用、無関係な object のエラー分離、device loss を制御した Task で確認する。

この段階では Portable queue、copy、shader、pipeline、描画、提出 batch の成功判定は実装対象に含めない。直接 root 入力の新しい Portable shader 実行試験は shader / command の段階で追加する。既存 legacy immediate shader 試験の成功を、その代わりには扱わない。
