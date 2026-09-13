# ADR 0017: Portable Resource のメモリ所有

## 状態

採用（目標設計）。Portable resource と内部 memory の共通の所有規約を定義する。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): 独立した backend と resource の直接生成。

## 決定

Buffer／Texture の生成は、resource object と必要な内部 memory の所有を一度に取得する。caller は resource の description を渡し、backend runtime が memory の取得と配置を処理する。公開 API に独立した heap、allocation、memory class、配置要件と配置 offset を設けない。

resource の破棄は内部 memory の所有も終了する。実際の memory の回収時期は backend runtime に従う。低層 wrapper が他 resource の寿命を追跡したり、完了待ちを補ったりする契約は設けない。

Buffer の byte 範囲と Texture の subresource は、その resource の内部を指定する値である。memory の確保単位や複数 resource 間の配置を表さない。package は resource 群の所有と寿命をまとめ、pool は再利用可能な resource object を管理する。

## API

| API | 説明 |
| --- | --- |
| `IPortableGpuBackend.CreateBuffer(description)`／`CreateTexture(description)` | resource と必要な内部 memory を一体で生成する。description には resource の形状と用途を渡す。 |
| `DestroyBuffer(buffer)`／`DestroyTexture(texture)` | caller が全利用終了を保証した resource を破棄し、その内部 memory の所有も終了する。別の memory 解放操作は不要。 |

この ADR は生成・破棄に共通する memory の所有規約を担当する。Buffer／Texture の description、mapping、copy footprint はそれぞれの resource API で定義する。

## 所有権

生成された handle のコピーは所有権を複製せず、resource を延命しない。caller は view／binding、未提出の記録、提出済みの GPU 利用を含む全利用を管理し、終了後に resource を一度だけ破棄する。runtime が診断できる usage や lifetime の条件を wrapper で再検証しない。

上位の manager は完了後の resource 回収を提供できる。memory 使用量を集計する場合は推定値と runtime が報告する値を区別し、resource の論理 size を実 allocation の容量や resident memory とみなさない。

## コード配置

以下は repository root からの配置で、後続機能の目標配置を含む。Portable project を新設し、WebGPU とそのテスト project に独立実装を加える。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/README.md` と `Device/IPortableGpuBackend.cs` | Buffer／Texture に共通する所有規約を記す。opaque identity は各 backend の派生型で持ち、共通の registry や公開 heap、allocation、memory manager は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable/Buffers/`、`src/graphics/Lumyte.Graphics.Portable/Textures/` | 公開の生成・破棄契約を各 resource の API と同じ場所に置く。共通所有規約のために別の生成入口を設けない。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Buffers/`、`src/graphics/Lumyte.Graphics.WebGPU/Textures/` | resource object と内部 memory の一体所有、非公開 handle、生成・破棄呼出しを resource ごとに置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Resources/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Resources/` | xUnit により fake runtime で生成・破棄の所有を確認し、実 device の resource 作成・終了試験は `Integration/` に隔離する。 |

上位の package、pool と完了後回収は Resource 管理 library に置き、低層 backend へ移さない。

## 使用例

`bufferDescription` と `textureDescription` はそれぞれの resource の生成設定とする。この例では GPU へ提出していない。

```csharp
var buffer = backend.CreateBuffer(bufferDescription);
try
{
    var texture = backend.CreateTexture(textureDescription);
    try
    {
        // resource を使用し、全利用の終了を caller が保証する。
    }
    finally
    {
        backend.DestroyTexture(texture);
    }
}
finally
{
    backend.DestroyBuffer(buffer);
}
```

## 採用範囲と未実装事項

resource と内部 memory の一体生成・破棄を Portable の唯一の所有モデルとする。独立した Portable 契約と native host の WebGPU backend に、Buffer／Texture の生成、native Destroy と参照解放を実装した。resource の opaque identity は実装側の非公開派生型が持ち、全 resource registry は設けない。

Bindings は内部 view／sampler への参照を所有し、破棄時に最後の参照を解放する。元 Buffer／Texture と Binding Layout の所有は caller に残す。上位 package／pool、自動退役、GPU command を含む寿命の接続と Browser backend は未実装である。生成・mapping・binding の試験を、まだ実装していない GPU 転送・描画の検証と扱わない。
