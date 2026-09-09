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
| `GpuTextureViewDimension` | 1D、2D、2D array、cube、cube array、3D の view 解釈。 |
| `GpuTextureAspect` | 全 aspect、depth のみ、stencil のみ。 |
| `GpuTextureViewDescription` | `Format`、`Dimension`、`Aspect`、`BaseMip`、`MipCount`、`BaseLayer`、`LayerCount`。 |
| `GpuTextureViewDescription.Normalize(textureDescription)` | caller の description から省略 format/range を解決する。host 算術だけを確認する。 |
| `GpuTextureView(Texture, Description)` | texture handle と view description の値。独立した所有権や identity は持たない。 |
| `GpuTextureView.Normalize(textureDescription)` | handle を保ち、正規化した description の値を返す。 |
| `GpuBufferRange(Buffer, Offset, Length)` | buffer 相対の byte 範囲。読み書きや要素型を固定しない。 |
| `GpuBufferRange.Normalize(bufferDescription)` | 省略 length を末尾まで解決する。device へ照会せず host の範囲算術を確認する。 |
| `GpuBufferRange.Slice(offset, length)` | 元の範囲を越えない部分範囲を作る。 |
| `GpuSamplerFilter` | `Nearest`、`Linear`。 |
| `GpuSamplerAddressMode` | `ClampToEdge`、`Repeat`、`MirrorRepeat`。 |
| `GpuSamplerDescription` | min/mag/mip filter、U/V/W address mode、min/max LOD、anisotropy、optional compare operation。 |
| `GpuAttachmentLoadOperation` | `Load` または `Clear`。 |
| `GpuAttachmentStoreOperation` | `Store` または `Discard`。 |
| `GpuClearColor`／`GpuClearDepthStencil` | color、depth、stencil の clear 値。 |
| `GpuColorAttachment` | texture view、load/store operation、clear color。 |
| `GpuDepthStencilAttachment` | view、depth/stencil 各 aspect の read-only 指定、nullable load/store、clear 値。read-only または存在しない aspect の operation は省略する。 |

comparison と format は ADR 0001 の基礎値 `GpuCompareOp`／`GpuFormat` を使う。これらの意味を共有することは、resource/view handle や shader byte layout の互換性を意味しない。

正規化は format compatibility、usage、dimension、sample count、filtering、resource lifetime を証明しない。runtime が検証できる条件は runtime に委ねる。`MutableFormat` は互換 format 再解釈の意図であり、depth/stencil の既定 aspect 選択にこの flag を要求しない。

## 所有権

caller は参照先 resource を最初の記録から全利用終了まで保持する。view/range を捨てても resource は解放されない。同じ値を複数 binding で使えるが、それぞれの利用完了を caller が管理する。必要な object の内部 cache は低層の変換処理であり、application resource の回収主体にはしない。

## コード配置

以下は repository root からの目標配置である。Portable とそのテスト project は新設予定、WebGPU とそのテスト project は既存を改編する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Views/` | 非 owning な texture view、buffer range、sampler description と正規化・slice の host 算術。 |
| `src/graphics/Lumyte.Graphics.Portable/Views/Attachments/` | color/depth-stencil attachment、load/store と clear の公開値。resource の生成・破棄 API と分離する。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Views/` | binding/attachment 実体化時の view・sampler 生成と内部 cache。公開 view identity や global descriptor 管理は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Views/` | 新設予定の xUnit project。省略値の正規化、range の slice と非 owning な値の振る舞いを検証する。 |
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

resource と解釈を分け、非 owning の値で扱う方針を採用する。Portable 専用の型、binding に合わせた view/sampler 実体化と cache、既存 validator の整理は未実装である。
