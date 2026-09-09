# ADR 0018: Portable Buffer API

## 状態

採用（目標設計）。

## 依存 ADR

- [0016 Portable Graphics API](0016-portable-api.md): backend と有効機能。
- [0017 Resource のメモリ所有](0017-resource-memory-model.md): resource と内部 memory の一体生成・破棄。

## 決定

Buffer は byte 配列を保持する Portable resource とする。uniform、storage、index、indirect、copy、mapping の使用意図を生成時に渡す。shader の構造体と要素の解釈は Buffer に固定しない。

Buffer の生成時に必要な memory も取得し、破棄時にその所有を終了する。生成前の heap 作成や配置計算は不要とする。shader からの参照には binding を用い、Native の GPU address を Portable Buffer の契約へ持ち込まない。

## API

| API | 説明 |
| --- | --- |
| `GpuBufferUsage` | `CopySource`、`CopyDestination`、`Uniform`、`Storage`、`Index`、`IndirectArguments`、`MapRead`、`MapWrite`。組合せの合法性は backend runtime に委ねる。 |
| `GpuBufferDescription(Size, Usage)` | byte size と利用用途。 |
| `GpuBufferHandle` | device に属する opaque identity。値のコピーは resource を所有・延命しない。 |
| `IPortableGpuBackend.CreateBuffer(description)` | memory を含む device-owned Buffer を作る。 |
| `MapBufferAsync(buffer, mode, offset, length)` | `GpuMapMode.Read` または `Write` で指定範囲を非同期に map し、`GpuMappedBufferRange` を返す。map 完了前や map 中に競合する GPU 利用をしない。 |
| `GpuMappedBufferRange.Memory` | map した host memory。読み書き権限は `GpuMapMode` に従う。shader からアクセスできる直接入力ではない。 |
| `GpuMappedBufferRange.Dispose()` | unmap し、すべての host memory 参照を無効化する。buffer 自体は破棄しない。 |
| `DestroyBuffer(buffer)` | 全利用終了後に Buffer を破棄し、内部 memory の所有も終了する。 |

mapping、binding と copy の offset は、生成した Buffer の先頭からの byte offset とする。caller が heap 内の位置を足し込む必要はない。

map は実際の resource 操作であり、永続的な CPU pointer を捏造しない。要求した resource の map に伴う runtime の待機はあり得るが、他 resource の所有や lifetime を引き受けない。Buffer を map できる用途と、GPU で利用できる用途の組合せは runtime の条件に従う。

Parameter Data の uniform/storage Buffer は caller または Resources が明示的に作り、必要な byte 列を転送する。command は root data を解析してこの Buffer を作成・更新しない。

## コード配置

以下は repository root からの目標配置である。Portable とそのテスト project は新設予定、WebGPU 関連 project は既存を改編する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable/Buffers/` | 公開 description、usage、handle、map mode、mapped range と backend の Buffer 操作契約。GPU address や heap 配置の型は置かない。 |
| `src/graphics/Lumyte.Graphics.WebGPU/Buffers/` | `GPUBuffer` の生成・破棄、非同期 map/unmap と mapped memory の所有処理。既存の Buffer 実装を移す。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Browser/Buffers/` | ブラウザーの mapped memory と promise の interop。JavaScript の参照や変換処理を Portable の公開型へ露出させない。 |
| `src/graphics/Lumyte.Graphics.Portable.Tests/Buffers/` | 新設予定の xUnit project。description と mapped range の公開契約を、device 不要の範囲で検証する。 |
| `src/graphics/Lumyte.Graphics.WebGPU.Tests/Buffers/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/Buffers/` | 既存 xUnit project の fake runtime による map/unmap と解放試験、実 device の mapping/copy 試験を分離する。 |

## 使用例

GPU 利用前の staging buffer にデータを用意する例である。`bytes` は map 範囲と同じ長さの byte 配列とする。

```csharp
var staging = backend.CreateBuffer(new GpuBufferDescription(
    (ulong)bytes.Length, GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
try
{
    using (var mapped = await backend.MapBufferAsync(
        staging, GpuMapMode.Write, 0, (ulong)bytes.Length))
    {
        bytes.CopyTo(mapped.Memory.Span);
    }
    // unmap 後、caller が copy command と完了までの寿命を管理する。
}
finally
{
    backend.DestroyBuffer(staging);
}
```

この例では command を提出していない。実際に提出した staging buffer は、その copy が完了してから破棄する。

## 採用範囲と未実装事項

WebGPU に適した明示用途と map/unmap を採用する。独立した Buffer API、内部 memory を含む生成・破棄、非同期 mapping は未実装である。実 device の制約の再実装や、Native address への変換 API は設けない。
