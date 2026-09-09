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
| `GpuBindingLayoutEntry` | group 内の `Binding`、`Visibility` と、次のいずれかの layout 値。 |
| `GpuBufferBindingLayout` | `Type`（Uniform／ReadOnlyStorage／Storage）、`MinBindingSize`、`HasDynamicOffset`。 |
| `GpuTextureBindingLayout` | `SampleType`（Float／UnfilterableFloat／Depth／Sint／Uint）、`ViewDimension`、`Multisampled`。 |
| `GpuStorageTextureBindingLayout` | `Access`（ReadOnly／WriteOnly／ReadWrite）、`Format`、`ViewDimension`。利用できる access は device feature に従う。 |
| `GpuSamplerBindingLayout` | `Type`（Filtering／NonFiltering／Comparison）。 |
| `GpuBindingLayoutHandle` | device に属する immutable な group layout の identity。 |
| `IPortableGpuBackend.CreateBindingLayout(entries)` | 一つの group の layout を作る。entry span は呼出し中に読み、必要な値を内部に保持する。 |
| `DestroyBindingLayout(layout)` | この layout を使う binding、program、pipeline と記録の利用終了後に解放する。 |

group 番号は program の layout 列の位置で決まる。同じ layout を複数 program で明示的に共有してよい。layout の異なる program へ resource の列を暗黙に並べ替える変換はしない。

layout の合法性、binding 数、visibility、sample/storage type と stage limit は runtime が検証する。wrapper は native 検証に加えて同じ validator を持たない。

## コード配置

以下は repository root からの目標配置である。Portable とそのテスト project は新設予定、WebGPU とそのテスト project は既存を改編する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Bindings/Layouts/` | 公開 layout entry、各 resource の layout 値、stage visibility、layout handle と生成・破棄契約。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Bindings/Layouts/` | `GPUBindGroupLayout` への変換、入力値の保持と内部 layout object の所有。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Bindings/Layouts/` | 新設予定の xUnit project。group の入力宣言を表す値の契約を CPU 上で検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Bindings/Layouts/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Bindings/Layouts/` | 既存 xUnit project。入力値を保持する生成経路は fake runtime、shader と layout の実接続は実 device で検証する。 |

shader package の group metadata は shader library に置き、低層 layout API から compiler を参照しない。WebGPU の layout validator はここで再実装しない。

## 使用例

`entries` は shader の一つの group に対応する明示 layout とする。ここではまだ resource を設定しない。

```csharp
GpuBindingLayoutHandle layout = backend.CreateBindingLayout(entries);
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

Portable の resource 入力を明示 layout とする。Bindless の profile や全候補列挙は採用しない。layout 型、shader package metadata と backend の接続は未実装である。
