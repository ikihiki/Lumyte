# ADR 0022: Portable Resource Binding API

## 状態

採用（目標設計）。

## 依存 ADR

- [0020 View API](0020-view-api.md): resource の非 owning な解釈。
- [0021 Binding Layout API](0021-binding-layout-api.md): group の入力宣言。

## 決定

layout に実 resource を設定した immutable な `GpuBindingsHandle` を用いる。一つの binding set は一つの group に対応する。draw/dispatch が必要とする group だけを command に明示設定する。

resource の変更は新しい binding set を作る。提出中の set の内容を書き換えず、device 全体の descriptor slot の割当・消去・再利用 API は設けない。Bindings は shader が索引を使って任意の resource を引く仕組みではない。

## API

| API | 説明 |
| --- | --- |
| `GpuBindingEntry.Buffer(binding, range)` | 指定 binding に `GpuBufferRange` を設定する。uniform/storage と dynamic offset の規則は layout に属する。 |
| `GpuBindingEntry.Texture(binding, view)` | 指定 binding に `GpuTextureView` を設定する。sampled/storage の条件は layout に属する。 |
| `GpuBindingEntry.Sampler(binding, description)` | 指定 binding に sampler description を設定する。 |
| `GpuBindingEntry.Binding`／`Kind` | group 内の binding 番号と `GpuBindingResourceKind`（Buffer／Texture／Sampler）。全ゼロの Undefined は resource 未設定の入力で、runtime が診断する。 |
| entry の `BufferRange`／`TextureView`／`SamplerDescription` | Kind が選んだ一つの resource 値。factory によって設定され、後から変更しない。 |
| `GpuBindingsHandle` | device に属する immutable な binding set の identity。shader-visible な integer index ではない。 |
| `IPortableGpuBackend.CreateBindings(layout, entries)` | layout と resource の対応を実体化する。入力値は内部へコピーし、必要な backend view/sampler/binding object を所有する。application resource の所有権は取得しない。 |
| `DestroyBindings(bindings)` | この set を使う未提出・提出済み command の利用終了後に object を解放する。参照先 resource は破棄しない。 |

`CreateBindings` の entries は `ReadOnlySpan<GpuBindingEntry>` とし、呼出し中にコピーする。`GpuBindingsHandle` は public abstract／protected constructor で外部 backend が非公開派生型を返す。layout、resource と内部 view/sampler の生成診断を、set 自身の生成診断と合わせて保持する。無効な依存 object を後から別の set が使っても、その診断を失わない。

各 entry は layout が要求する一つの入力に対応する。実際に必要な resource を渡す。未使用の global slot を埋める処理、fallback resource の自動挿入、全 device resource の保持は行わない。

dynamic offset は set 自体を書き換えず command 側で渡す。layout で指定した dynamic buffer binding の番号順に対応し、range の基準 offset に加算する。range、alignment、usage と layout の適合性は runtime に委ねる。

低層では caller が layout、resource、binding set を最初の記録から GPU の全利用終了まで保持する。immutable な set を複数提出から再利用できる。上位の Resources はこの API の上で set の cache と参照先の保持を引き受けられる。

## コード配置

以下は repository root からの配置で、後続の command 接続の目標配置を含む。Portable とそのテスト project、WebGPU の独立実装に追加する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Bindings/` | 公開 `GpuBindingEntry`、`GpuBindingsHandle` と immutable set の生成・破棄契約。layout 定義は配下の `Layouts/` に分ける。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Bindings/` | `GPUBindGroup` の生成・破棄と、set が所有する内部 view/sampler 参照の保持。application resource の所有権は取得しない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Bindings/` | 外部 backend の consumer test で、Buffer／Texture／Sampler の入力、layout と set の生成・明示的な破棄を検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Bindings/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Bindings/` | 既存 xUnit project。set の不変性と内部 object の所有を fake runtime で、group 設定から draw/dispatch までを実 device で検証する。 |

dynamic offset の記録は command の担当とし、上位の material/pass 向け binding cache は Resource 管理 library に置く。

## 使用例

`layout` は binding 0 に sampled texture、1 に sampler を要求する生成済み layout、`view` と `sampler` は対応する値とする。

```csharp
var bindings = backend.CreateBindings(layout, new[]
{
    GpuBindingEntry.Texture(0, view),
    GpuBindingEntry.Sampler(1, sampler),
});
try
{
    // command の group に設定し、全利用の完了まで保持する。
}
finally
{
    backend.DestroyBindings(bindings);
}
```

## 参考文献

- [WebGPU: Bind Groups](https://gpuweb.github.io/gpuweb/#bind-groups): layout と実 resource を対応付ける object。

## 採用範囲と未実装事項

Portable に明示 binding を採用する。専用 handle、immutable set の作成・解放、依存 object の生成診断の保持、使用中の binding だけで共有する view/sampler cache を実装した。最後の binding 参照がなくなった内部 object は cache から取り除き、参照先の application resource は破棄しない。

compute command への group 設定、dynamic offset の実行と shader からの参照を接続した。raster と Browser 接続は未実装である。Native の descriptor storage を共通化する互換経路は追加しない。[進捗記録](../designs/graphics-implementation-progress.md)
