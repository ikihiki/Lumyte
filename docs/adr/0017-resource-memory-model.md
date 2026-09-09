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

以下は repository root からの目標配置である。Portable project は新設予定、WebGPU とそのテスト project は既存を改編する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Resources/` | Buffer／Texture に共通する所有規約の文書化と、必要な内部 resource identity 処理を置く。公開 heap、allocation、memory manager は追加しない。 |
| `src/graphics/Lumyte.Graphics.Portable/Buffers/`、`src/graphics/Lumyte.Graphics.Portable/Textures/` | 公開の生成・破棄契約を各 resource の API と同じ場所に置く。共通所有規約のために別の生成入口を設けない。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Resources/` | resource object と内部 memory の一体所有に関する内部 handle 管理。個々の WebGPU 生成・破棄呼出しは同 project の `Buffers/`、`Textures/` に置く。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Resources/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Resources/` | xUnit により fake runtime で生成・破棄の所有を確認し、実 device の resource 作成・終了試験は `Integration/` に隔離する。 |

上位の package、pool と完了後回収は Resource 管理 library に置き、この低層の `Resources/` へ移さない。

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

resource と内部 memory の一体生成・破棄を Portable の唯一の所有モデルとする。独立した Portable backend と各 resource の生成・破棄経路への接続は未実装である。
