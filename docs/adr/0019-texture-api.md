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
| `GpuTextureCopyFootprint` | mip、aspect、origin、extent、row/image pitch によって buffer 内の byte 表現と texture 領域の対応を表す。 |
| `GpuTextureCopyFootprint.RequiredBytes` | footprint の必要 byte 数を host で計算する。整数 overflow を防ぎ、device の format/usage/pitch 条件は再検証しない。 |
| `DestroyTexture(texture)` | 全利用終了後に Texture を破棄し、内部 memory の所有も終了する。 |

copy footprint は転送に用いる Buffer 内の byte 配列を表し、Texture の内部 memory 配置を表さない。複数 Texture の package は resource の管理単位とする。

## コード配置

以下は repository root からの目標配置である。Portable とそのテスト project は新設予定、WebGPU とそのテスト project は既存を改編する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Textures/` | 公開 description、dimension、usage、handle と copy footprint。`RequiredBytes` の host 算術もここに置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Textures/` | `GPUTexture` の生成・破棄、description 変換と `MutableFormat` に応じた内部 view format の設定。画像ファイルの読み込みや復号は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Textures/` | 新設予定の xUnit project。copy footprint の byte 数と overflow など host 上の振る舞いを検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Textures/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Textures/` | 既存 xUnit project。fake runtime への生成設定と破棄の接続を検証し、実 device の copy・format 再解釈試験を隔離する。 |

## 使用例

`description` は sampled texture の生成 description とする。生成は binding の作成や slot 割当を伴わない。

```csharp
var texture = backend.CreateTexture(description);
try
{
    // caller が view、転送、描画とそれぞれの完了を管理する。
}
finally
{
    backend.DestroyTexture(texture);
}
```

## 採用範囲と未実装事項

Texture と内部 memory の一体生成・破棄を採用する。新しい独立 backend、Texture の生成・破棄と copy footprint の接続は未実装である。
