# ADR 0021: Portable Binding Layout API

## 状態

採用（目標設計）。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): 独立した明示 binding model。
- [0020 View API](0020-view-api.md): texture dimension、sampler と format の解釈。

## 決定

shader の resource 入力は group と binding 番号を持つ有限の layout で定義する。layout は一つの group に必要な buffer、texture、sampler の型と使用条件を宣言する。実 resource は保持しない。

layout の範囲は shader program が使う入力である。device 全体の descriptor 空間、固定 slot 容量、Bindless profile は作らない。Native の descriptor heap や address ABI を layout へ変換しない。

## API

| API | 説明 |
| --- | --- |
| `GpuShaderStage` | `Vertex`、`Pixel`、`Compute` の stage 集合を visibility に使う。`Pixel` は WebGPU の fragment stage に対応する。 |
| `GpuBindingLayoutEntry` | group 内の `Binding`、`Visibility` と一つの layout 値を constructor へ渡す不変の値。4種類の layout ごとに overload を持つ。 |
| `GpuBindingLayoutKind`／entry の `Kind` | `Buffer`、`Texture`、`StorageTexture`、`Sampler` が選択された layout を示す。全ゼロ値の `Undefined` は入力未設定を表し、その合法性は runtime が診断する。 |
| entry の `BufferLayout`／`TextureLayout`／`StorageTextureLayout`／`SamplerLayout` | Kind に対応する一つだけが有効な値。他の member は未選択の既定値である。 |
| `GpuBufferBindingLayout` | `Type`（Uniform／ReadOnlyStorage／Storage）、`MinBindingSize`、`HasDynamicOffset`。 |
| `GpuTextureBindingLayout` | `SampleType`（Float／UnfilterableFloat／Depth／Sint／Uint）、`ViewDimension`、`Multisampled`。 |
| `GpuStorageTextureBindingLayout` | `Access`（ReadOnly／WriteOnly／ReadWrite）、`Format`、`ViewDimension`。利用できる access は device feature に従う。 |
| `GpuSamplerBindingLayout` | `Type`（Filtering／NonFiltering／Comparison）。 |
| `GpuBindingLayoutHandle` | device に属する immutable な group layout の identity。 |
| `IPortableGpuBackend.CreateBindingLayout(entries)` | 一つの group の layout を作る。entry span は呼出し中に読み、必要な値を内部に保持する。 |
| `DestroyBindingLayout(layout)` | この layout を使う binding、program、pipeline と記録の利用終了後に解放する。 |

`GpuShaderStage` は flags で、`None` は0、Vertex／Pixel／Computeを組み合わせる。型の分類値は `GpuBufferBindingType`、`GpuTextureSampleType`、`GpuStorageTextureAccess`、`GpuSamplerBindingType` として公開し、各 layout の表に記した値を持つ。buffer の MinBindingSize は ulong、既定0、HasDynamicOffset は既定false。sampled/storage texture の ViewDimension は既定Texture2D、Multisampled は既定falseとする。

`CreateBindingLayout` は `ReadOnlySpan<GpuBindingLayoutEntry>` を呼出し中にコピーする。公開 handle は public abstract 基底型と protected constructor から外部 backend が実装し、source span の変更や解放に依存しない。生成した object の非同期診断は内部に保持し、利用する binding／program／batch の診断へ引き継ぐ。返却された handle 自体は runtime の成功確定を意味しない。

group 番号は program の layout 列の位置で決まる。同じ layout を複数 program で明示的に共有してよい。layout の異なる program へ resource の列を暗黙に並べ替える変換はしない。

layout の合法性、binding 数、visibility、sample/storage type と stage limit は runtime が検証する。wrapper は native 検証に加えて同じ validator を持たない。

## コード配置

以下は repository root からの配置で、後続の shader 接続の目標配置を含む。Portable とそのテスト project、WebGPU の独立実装に追加する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Bindings/Layouts/` | 公開 layout entry、各 resource の layout 値、stage visibility、layout handle と生成・破棄契約。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Bindings/Layouts/` | `GPUBindGroupLayout` への変換、入力値の保持と内部 layout object の所有。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Bindings/` | 外部 backend の consumer test で、4種類の layout 宣言と不透明 handle の生成・破棄を検証する。layout 固有の追加試験は配下の `Layouts/` に置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Bindings/Layouts/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Bindings/Layouts/` | 既存 xUnit project。入力値を保持する生成経路は fake runtime、shader と layout の実接続は実 device で検証する。 |

shader package の group metadata は shader library に置き、低層 layout API から compiler を参照しない。WebGPU の layout validator はここで再実装しない。

## 使用例

一つの group に read-only storage buffer を宣言する。ここではまだ resource を設定しない。

```csharp
GpuBindingLayoutHandle layout = backend.CreateBindingLayout([
    new GpuBindingLayoutEntry(0, GpuShaderStage.Compute,
        new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage,
            MinBindingSize: 16, HasDynamicOffset: true)),
]);
try
{
    // shader program と resource binding の構築に使う。
}
finally
{
    backend.DestroyBindingLayout(layout);
}
```

## 参考文献

- [WebGPU: Bind Group Layouts](https://gpuweb.github.io/gpuweb/#bind-group-layouts): group ごとの binding 宣言。

## 採用範囲と未実装事項

Portable の resource 入力を明示 layout とする。layout の公開値、opaque handle、入力列の保持、native host の GPUBindGroupLayout 生成・解放と非同期診断を実装した。binding の重複番号や visibility、limit、layout の合法性は runtime に委ねる。

compute shader module／pipeline と直接入力の layout へ接続した。raster、shader package metadata と Browser の接続は未実装である。[進捗記録](../designs/graphics-implementation-progress.md)
