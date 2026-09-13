# ADR 0019: Portable Texture API

## 状態

採用（目標設計）。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): backend と capability。
- [0017 Resource のメモリ所有](0017-resource-memory-model.md): resource と内部 memory の一体生成・破棄。

## 決定

Texture は extent、format、mip/layer、sample count と使用用途を持つ resource とする。必要な memory を含めて生成し、Texture の破棄でその所有も終了する。生成前の heap 作成や配置計算は不要とし、view と binding の作成は独立した操作とする。

format 再解釈の意図には `MutableFormat` を用いる。許可 format の列挙を公開 API に追加しない。実際に利用できる解釈は backend の format 規則に従い、View 側で使う format を指定する。

## API

| API | 説明 |
| --- | --- |
| `GpuTextureDimension` | resource の `Texture1D`、`Texture2D`、`Texture3D`。 |
| `GpuTextureUsage` | `Sampled`、`Storage`、`ColorAttachment`、`DepthStencilAttachment`、`CopySource`、`CopyDestination`。 |
| `GpuTextureDescription` | `Dimension`、`Width`、`Height`、`Depth`、`MipCount`、`LayerCount`、`SampleCount`、`Format`、`Usage`、`MutableFormat`。 |
| `GpuTextureDescription.MutableFormat` | 互換 format による view を許可する bool。既定は false。depth/stencil の aspect 選択とは別の条件。 |
| `GpuTextureHandle` | device に属する opaque identity。値のコピーは resource を所有・延命しない。 |
| `IPortableGpuBackend.CreateTexture(description)` | memory を含む device-owned Texture を作る。 |
| `GpuTextureCopyFootprint(Mip, Aspect, Origin, Extent, RowPitch = 0, ImagePitch = 0)` | buffer 内の byte 表現と texture 領域の対応を表す値。`Origin` は `GpuOrigin3D`、`Extent` は `GpuExtent3D`、pitch は ulong の byte 単位。 |
| `GpuTextureCopyFootprint.RequiredBytes(format)` | 引数の `GpuFormat` と aspect から必要な byte 範囲を host で計算する。footprint 自体は resource や重複した format を保持しない。整数 overflow を防ぎ、device の format/usage/pitch 条件は再検証しない。 |
| `DestroyTexture(texture)` | 全利用終了後に Texture を破棄し、内部 memory の所有も終了する。 |

copy footprint は転送に用いる Buffer 内の byte 配列を表し、Texture の内部 memory 配置を表さない。複数 Texture の package は resource の管理単位とする。

`Origin.Z` と `Extent.Depth` は、2D texture では array layer、3D texture では depth slice を表す。別の layer 指定と二重に保持しない。`RowPitch` は texel block row の間隔、`ImagePitch` は layer／depth slice の間隔であり、0 は backend の copy descriptor への指定を省略する。実行上必要な pitch は caller が与え、省略値が合法かは runtime が検証する。

`RequiredBytes(format)` は行・画像間の隙間を含み、最後の行の末尾 padding を含まない。算術上の省略 pitch は密に詰めた byte 数として計算するが、GPU への pitch の暗黙補完や再配置は行わない。`Depth24PlusStencil8` の stencil aspect は 1 byte/texel で計算できる。depth aspect の byte 表現は固定できないため、この計算メソッドは `NotSupportedException` とする。byte 表現が存在しない aspect の copy 指定自体は native runtime へ渡し、copy の可否を独自に判定しない。

texture 間 copy は二つの領域の extent を一致させ、buffer 用の pitch は使わない。Buffer と texture の copy は buffer range の先頭 offset をそのまま用い、論理 range が `RequiredBytes` を含むことを確認する。backend の整数幅や stride 表現への変換で caller の値を失う指定は拒否するが、alignment、実 resource の境界、usage、mip、aspect や format の組合せの合法性は runtime に委ねる。

## コード配置

以下は repository root からの配置。Portable の公開契約と WebGPU の native host 実装を分離する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Textures/` | 公開 description、dimension、usage、handle と copy footprint。`RequiredBytes` の host 算術もここに置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Textures/` | `GPUTexture` の生成・破棄、description 変換と `MutableFormat` に応じた内部 view format の設定。画像ファイルの読み込みや復号は追加しない。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Commands/` | footprint の値を texture copy descriptor へ変換する記録・encode。resource の生成処理や画像 decoder は置かない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Textures/` | 必要 byte 数、padding、aspect、省略 stride と overflow の host 算術を検証する。生成・破棄の公開契約は `Device/` の consumer test で検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Textures/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Textures/` | 生成設定、破棄、実 device の texture 生成・format 再解釈試験を隔離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Commands/` | 2D array／3D の mip・origin、異なる row/image pitch の転送、stencil aspect、論理 range と native copy 診断の実機試験。 |

## 使用例

4×4 の sampled texture と、その upload に用いる footprint を作る。生成は binding の作成や slot 割当を伴わない。この例は resource と転送 byte 数の準備までとし、command は提出しない。

```csharp
var description = new GpuTextureDescription(
    GpuTextureDimension.Texture2D, 4, 4, 1, 1, 1, 1,
    GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
var footprint = new GpuTextureCopyFootprint(
    0, GpuTextureAspect.All, default, new GpuExtent3D(4, 4, 1),
    RowPitch: 256, ImagePitch: 1024);
ulong uploadBytes = footprint.RequiredBytes(description.Format); // 784 byte
var texture = backend.CreateTexture(description);
try
{
    // upload Buffer の range は uploadBytes 以上を用意する。
    // caller が view、転送、描画と全利用終了までの寿命を管理する。
}
finally
{
    backend.DestroyTexture(texture);
}
```

## 採用範囲と未実装事項

Texture と内部 memory の一体生成・破棄を採用する。独立した Portable 契約と native host の WebGPU backend に、1D／2D／3D description、opaque handle、生成・破棄、MutableFormat と object ごとの非同期生成診断を実装した。

View 値からの内部 view 生成と sampled／storage texture の Binding、compute／raster pass への group 設定、raster attachment、copy footprint と buffer↔texture／texture 間 copy を実装した。実機で 2D array／3D の mip・origin と異なる row/image pitch、stencil の upload／readback、sampled texture の raster 利用、attachment の描画と readback を検証する。

現在の `GpuFormat` には圧縮 format がなく、その転送は未実装である。内部 texture placement の照会・公開や画像ファイルの loading は追加しない。Browser 実装は未実装である。実機検証の結果と範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。
