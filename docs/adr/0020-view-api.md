# ADR 0020: Portable View API

## 状態

採用（目標設計）。

## 依存 ADR

- [0001 Graphics の基礎値](0001-graphics-api.md): format と comparison の値。
- [0016 Portable Graphics API](0016-portable-api.md): Portable の型と所有権。
- [0018 Buffer](0018-buffer-api.md): buffer handle と description。
- [0019 Texture](0019-texture-api.md): texture handle と description。

## 決定

texture view は resource とその解釈を組み合わせた非 owning の値にする。buffer は byte 範囲、sampler は description の値で表す。値の構築とコピーは GPU object を生成せず、resource を延命しない。

binding や attachment を実体化するときに backend が必要な view/sampler object を用意する。public view identity、view の生成・破棄操作、descriptor index は要求しない。resource の読み書きは binding と pass の使用条件に属する。

## API

| API | 説明 |
| --- | --- |
| `GpuTextureViewDimension` | `Texture1D`、`Texture2D`、`Texture2DArray`、`Cube`、`CubeArray`、`Texture3D` の view 解釈。 |
| `GpuTextureAspect` | `All`、`DepthOnly`、`StencilOnly`。 |
| `GpuTextureViewDescription` | `Format`、`Dimension`、`Aspect`、`BaseMip`、`MipCount`、`BaseLayer`、`LayerCount`。format、dimension と count は nullable で、null だけが省略。base は0、aspect は All が既定。 |
| `GpuTextureViewDescription.Normalize(textureDescription)` | caller の description から省略 format/range を解決する。host 算術だけを確認する。 |
| `GpuTextureView(Texture, Description)` | texture handle と view description の値。独立した所有権や identity は持たない。 |
| `GpuTextureView.Normalize(textureDescription)` | handle を保ち、正規化した description の値を返す。 |
| `GpuBufferRange(Buffer, Offset = 0, Length = null)` | buffer 相対の ulong offset と nullable ulong length。null は末尾まで、0は明示した空範囲。読み書きや要素型を固定しない。 |
| `GpuBufferRange.Normalize(bufferDescription)` | 省略 length を末尾まで解決する。device へ照会せず host の範囲算術を確認する。 |
| `GpuBufferRange.Slice(offset, length = null)` | length が確定した元範囲から部分範囲を作る。null は残りの範囲。元の length が未確定なら先に Normalize する。返す範囲の開始・終端の overflow も確認する。 |
| `GpuSamplerFilter` | `Nearest`、`Linear`。 |
| `GpuSamplerAddressMode` | `ClampToEdge`、`Repeat`、`MirrorRepeat`。 |
| `GpuSamplerDescription` | `MinFilter`／`MagFilter`／`MipFilter`、`AddressU`／`V`／`W`、`MinLod`／`MaxLod`、uint の `MaxAnisotropy`、nullable `Compare`。`new()` は nearest、clamp、LOD 0..32、anisotropy 1、comparison なし。全ゼロの `default` struct は明示0を保持し、runtime が診断する。 |
| `GpuAttachmentLoadOperation` | `Load` または `Clear`。 |
| `GpuAttachmentStoreOperation` | `Store` または `Discard`。 |
| `GpuClearColor(Red, Green, Blue, Alpha)`／`GpuClearDepthStencil(Depth, Stencil)` | double 4成分の color、float depth と uint stencil の clear 値。 |
| `GpuColorAttachment` | `View`、`LoadOperation`、`StoreOperation`、`ClearColor`。 |
| `GpuDepthStencilAttachment` | `View`、`DepthReadOnly`／`StencilReadOnly`、nullable の `DepthLoadOperation`／`DepthStoreOperation`／`StencilLoadOperation`／`StencilStoreOperation`、`ClearValue`。read-only または存在しない aspect の operation は省略する。 |

comparison と format は ADR 0001 の基礎値 `GpuCompareOp`／`GpuFormat` を使う。これらの意味を共有することは、resource/view handle や shader byte layout の互換性を意味しない。

Normalize は caller が渡した生成 description から format と dimension を補い、mip の省略 count は残りを選ぶ。layer の省略 count は1D／2D／3Dなら1、cubeなら6、arrayなら残りを選ぶ。2D texture の既定 view dimension は layer が複数なら2D arrayとする。`Depth24PlusStencil8` と単一 aspect の組は論理 format と aspect を保持し、backend が native の aspect 固有 format に接続する。

正規化は format compatibility、usage、dimension、sample count、filtering、resource lifetime を証明しない。runtime が検証できる条件は runtime に委ねる。`MutableFormat` は互換 format 再解釈の意図であり、depth/stencil の既定 aspect 選択にこの flag を要求しない。

## 所有権

caller は参照先 resource を最初の記録から全利用終了まで保持する。view/range を捨てても resource は解放されない。同じ値を複数 binding で使えるが、それぞれの利用完了を caller が管理する。必要な object の内部 cache は低層の変換処理であり、application resource の回収主体にはしない。

## コード配置

以下は repository root からの配置で、後続の attachment 実行経路の目標配置を含む。Portable とそのテスト project、WebGPU の独立実装に追加する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Views/` | 非 owning な texture view、buffer range、sampler description と正規化・slice の host 算術。 |
| `src/graphics/Lumyte.Graphics.Portable/Views/Attachments/` | color/depth-stencil attachment、load/store と clear の公開値。resource の生成・破棄 API と分離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Views/` | binding/attachment 実体化時の view・sampler 生成と内部 cache。公開 view identity や global descriptor 管理は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Views/` | 隣接する xUnit project。省略値の正規化、range の slice と非 owning な値の振る舞いを検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Views/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Views/` | 既存 xUnit project。内部 object の再利用・解放を fake runtime で、実 view/sampler の利用を実 device で検証する。 |

共有 format/comparison の定義は基礎値の project に残し、このディレクトリで複製しない。

## 使用例

`texture`、`buffer` と生成時 description は caller が保持しているものとする。

```csharp
var view = new GpuTextureView(texture, viewDescription)
    .Normalize(textureDescription);
var range = new GpuBufferRange(buffer, 0, 256)
    .Normalize(bufferDescription);
var attachment = new GpuColorAttachment(
    view, GpuAttachmentLoadOperation.Clear,
    GpuAttachmentStoreOperation.Store, new GpuClearColor(0, 0, 0, 1));
```

## 採用範囲と未実装事項

resource と解釈を分け、非 owning の値で扱う方針を採用する。Portable 専用の view／range／sampler／attachment 値、Normalize／Slice の host 算術、native host の binding 用 view/sampler 実体化を実装した。内部 cache は使用中の binding が共有し、最後の参照の解放で取り除く。

attachment の command 実体化、sampling／描画結果と Browser 接続は未実装である。現在の実機試験は binding の生成・診断・内部 object の所有を確認するもので、shader からの利用成功とは区別する。[進捗記録](../designs/graphics-implementation-progress.md)
