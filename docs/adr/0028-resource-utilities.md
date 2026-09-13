# ADR 0028: Native／Portable の Resource Utilities

## 状態

採用（目標設計）。

Native の arena、Portable の Buffer／Texture pool と明示的な貸出・返却を実装済みとする。completion token、自動退役と転送 utility は後続であり、この文書の目標 API に含めて記載する。

Native と Portable に独立した utility を設ける。Native は native heap と linear region、Portable は自身の Buffer／Texture と提出経路を使う。両 utility が共通 backend interface を介して動くことは要求しない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics 基礎](0001-graphics-api.md) | 二系統の責務、caller lifetime、native validation |
| [0003 Native Memory Allocation](0003-native-memory-allocation-api.md)・[0004 Native Linear Data](0004-native-linear-data-api.md)・[0005 Native Texture](0005-native-texture-api.md) | 統一 heap、requirement、region と texture の配置 |
| [0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md) | Native command の受理と完了 |
| [0016 Portable API](0016-portable-api.md)・[0017 Portable Resource のメモリ所有](0017-resource-memory-model.md) | Portable device とメモリ込みの resource 生成・解放 |
| [0018 Portable Buffer](0018-buffer-api.md)・[0019 Portable Texture](0019-texture-api.md) | device-owned resource と転送 |
| [0025 Portable Command](0025-command-recording-api.md)・[0026 Portable 提出と同期](0026-command-submission-and-synchronization.md) | Portable command の受理と完了 |

## 決定

`Lumyte.Graphics.Native.Resources` と `Lumyte.Graphics.Portable.Resources` にそれぞれ必要な機構を実装する。公開する同名 utility 型も別の型であり、Native の token、memory、command を Portable に渡せない。共通化できる CPU の範囲演算や retirement データ構造は内部で共有してよい。

これらは低層を直接使う caller と各機能 pass 実装のための API である。共通 RenderGraph の consumer は機能 pass に CPU 入力と論理 I/O を渡し、この utility の resource、転送 token、completion 型を受け取らない。pass 本体が準備済み CPU data の GPU 配置と upload を明示して行い、provider が共通の論理 resource／completion と自系統の値を対応付ける。utility 側には共通 Graph の型への依存を加えない。

utility は caller が明示的に指定した memory 範囲、転送、completion だけを扱う。resource の所有関係、shader の参照先、pass の依存を推論しない。[No Graphics API](https://github.com/sebbbi/NoGraphicsAPI) の allocation と上位 allocator の分離を Native に適用し、Portable には自身の resource model に適した pool を用いる。

## API

### Native の memory arena

| API | 説明 |
| --- | --- |
| `GpuMemoryArena(nativeDevice, blockSize)` | Native device を借用し、純粋な `NativeGpuHeap` block とその貸出範囲を所有する。 |
| `Allocate(size, alignment, kind, compatibilities)` | 同じ取得済み requirement token の参照 identity の集合と memory kind に対応する pool から、整列した `GpuMemorySlice` を貸し出す。token の順序・重複は集合の意味を変えない。新規 block では集合内の全 token を `CreateGpuHeap` に渡す。 |
| `GpuMemorySlice.Heap / Offset / Size` | backing heap、heap 基準の offset、予約 byte 数を持つ貸出 identity。public constructor は持たず、同じ範囲の再貸出でも新しい object にする。region や texture の生成は caller が行う。 |
| `Release(slice)` | 全参照の終了と配置 resource の破棄を caller が保証した範囲を返す。 |
| `Retire(slice, completion)` | 最終利用を覆う同じ系統の completion まで返却を遅らせる。 |
| `Collect()` | 完了した退役範囲を再利用可能にする。待機しない。 |
| `Trim()` | 完全に空いた heap block を解放する。 |
| `Dispose()` | 貸出と退役がすべて終了した arena を破棄する。残る範囲の強制解放は行わない。 |

arena は texture pool と linear pool の block を同じ heap 型で確保する。用途別に pool を分けることと、allocation API に別の heap 型を設けることは区別する。caller が mixed allocation を要求した場合は全 requirement token を一度の確保へ渡す。共通 memory の選択は Native backend に任せ、管理層で token の bit 表現を再解釈しない。

参照 identity は取得済み token を再利用するための key とし、token の `Equals` や分類値から native 適合性を推定しない。別の取得で返された token は、同じ native 条件に見えても別集合として扱う。caller が requirement を保持して再利用する。block は要求以上で要求 alignment の倍数となる整列条件を持つものだけを再利用し、各 slice の開始を独立に整列させる。未使用範囲は分割し、返却した隣接範囲を結合する。新規 block は blockSize と要求 Size の大きい方を要求 Alignment へ切り上げる。size と alignment は非ゼロとし、CPU の範囲計算の overflow を拒否する。計算自体は正の alignment を扱い、native が受け付ける alignment の制約を独自に増やさない。

placement に渡す予約量は Native が返した `requirements.Size`／`Alignment` に従う。混在配置の境界 padding は Native の requirement に含まれており、arena が resource 分類や Vulkan の granularity を照会して重ねて計算しない。新しい resource を配置した場合に、以前の alias の内容を引き継ぐとは保証しない。

arena の slice は native allocation の別名ではない。heap 全体を解放する権限は arena にあり、caller は slice を `DestroyGpuHeap` へ渡さない。配置には `slice.Offset + localOffset` を一度だけ加え、linear region 内の offset と混同しない。

### Portable の resource pool

| API | 説明 |
| --- | --- |
| `GpuBufferPool(backend)`／`GpuTexturePool(backend)` | `IPortableGpuBackend` を借用し、完全な resource description と用途に従って resource を所有・再利用する。 |
| `Acquire(description)` | 使用可能な同条件の resource を、貸出ごとに新しい `GpuBufferLease`／`GpuTextureLease` に入れて返す。なければ Portable の生成 API で作る。 |
| `GpuBufferLease.Handle / Description`／`GpuTextureLease.Handle / Description` | 借用する raw handle と完全な生成 description。lease は pool が発行し、コピーしても貸出を増やさない。Handle の取得は返却まで可能で、Description は返却後も不変 metadata として読める。 |
| `Release(lease)` | mapping、binding、未提出参照と GPU 利用が終了した当該貸出を返す。別 pool・返却済みの lease は拒否する。 |
| `Retire(lease, completion)` | Portable の completion まで当該貸出の返却を遅らせる。 |
| `Collect()`／`Trim()` | 完了した貸出を回収する／使用していない resource を破棄する。どちらも GPU 待機を行わない。 |
| `Dispose()` | 全貸出・退役が終了した pool を破棄する。 |

Portable はメモリを含む Buffer／Texture object を貸出・再利用する。pool の補充には `CreateBuffer`／`CreateTexture`、未使用 object の解放には対応する `Destroy` を使う。独立した heap の作成や placement は経由しない。buffer 内の range を分けて使う場合も、それは一つの Buffer の byte 範囲であり、独立した memory allocation の貸出ではない。

pool が resource を再貸出ししても、内容や GPU state が生成直後へ戻ったとはみなさない。初期化、先行利用との同期、次の利用に必要な状態は caller が処理する。

再利用条件には Buffer の Size／Usage と、Texture の dimension、各寸法、mip/layer/sample 数、format、usage、MutableFormat をすべて含める。用途の包含関係から大きい resource を代用したり、descriptor を補正したりしない。値をそのまま backend に渡し、GPU の合法性は runtime の診断に委ねる。

raw handle 自体は再利用するため、`Release(handle)` では古い貸出と新しい貸出を区別できない。返却には lease identity を使用し、古い lease が同じ handle の新しい貸出を返せないようにする。lease に自動 Dispose／finalizer による返却は設けない。caller は保存済み raw handle も含む全利用を終了してから明示的に返す。

### CPU 転送（未実装）

両 namespace に同名の `GpuTransferUtilities` を置く。引数は系統ごとの raw resource、copy footprint、同期情報を使う。

| API | 説明 |
| --- | --- |
| `WriteBufferAsync(device, destination, offset, source, synchronization, cancellationToken)` | CPU byte 列を buffer の指定範囲へ転送し、完了を非同期に待つ。Native の destination は linear range、Portable は Buffer とする。 |
| `ReadBufferAsync(device, source, synchronization, cancellationToken)` | 指定した範囲を CPU byte 列へ readback し、完了を非同期に待つ。 |
| `WriteTextureAsync(device, destination, footprint, source, synchronization, cancellationToken)` | footprint に対応する texture 範囲を upload し、完了を非同期に待つ。 |
| `ReadTextureAsync(device, source, footprint, synchronization, cancellationToken)` | texture の指定範囲を CPU byte 列へ readback し、完了を非同期に待つ。 |

`synchronization` の型は系統ごとに定義する。Native は caller が指定した before/after state と依存に従う。Portable は Portable command と queue の契約に従い、WebGPU では API が管理する resource transition を二重に管理しない。どちらも await の正常終了は、GPU 使用終了と当該転送の診断を含む成功確認を意味する。Portable は queue 到達だけを根拠に成功した readback bytes を返さない。browser の event loop を止める blocking wait を要求しない。

Native の CPU mapping は linear region を通じて行う。Portable の staging、queue write、map はその backend の経路を使う。CPU の byte 範囲、pitch、overflow は utility が確認し、GPU の format、usage、offset alignment、resource state の合法性は native API と validation に委ねる。command に Parameter Data の生成や root data の buffer fallback を追加しない。

### Completion と retirement（未実装）

| API | 説明 |
| --- | --- |
| `GpuSubmissionToken` | 発行元と受理済み submission を識別する非所有 token。系統ごとの別型であり、default は無効。 |
| `IsValid / IsComplete` | 発行済みか／発行元を通じて GPU 使用が終了したかを返す。IsComplete は処理成功の証明ではなく、無効 token は使用終了の証明にもならない。 |
| `WaitAsync(cancellationToken)` | 対応 submission の使用終了と診断の確定を非同期に待ち、失敗を伝える。cancellation は CPU の待機だけを終了する。 |
| `Equals / GetHashCode / == / !=` | 発行元と submission の両方で比較する。 |
| `GpuRetirementQueue(device)` | 同じ系統の明示 completion と回収処理を保持する。device を借用する。 |
| `Retire(completion, release)` | completion が指定対象の全利用を覆うことを caller が保証して、回収処理を登録する。 |
| `Collect()` | 完了した登録を非 block で実行する。 |
| `WaitIdleAsync() / DisposeAsync()` | この queue が登録した work と回収を非同期に drain する。device 全体の idle は保証しない。 |
| `GpuSubmissionException.Completion` | 受理後に失敗した処理の completion を通知する。例外自体は resource を所有しない。 |

上位実行器が提出と token 発行を結び付ける。retirement queue は raw 提出を自動登録しない。複数 queue にまたがる利用は、必要な全 completion またはそれらを同期してまとめた completion で覆う。別の owner の数値を同じ timeline として比較しない。

回収は GPU 使用終了に従い、転送結果の公開は WaitAsync の成功に従う。失敗した転送でも使用が終了していれば staging を回収できるが、転送先の内容が完成したとは扱わない。Portable の下位 GpuExecutionException は診断を保って上位の待機へ接続する。Resources の GpuSubmissionException.Completion は保持の回収に必要な token を通知するものであり、下位例外に上位の token 型を参照させない。

## 所有権と失敗

utility は自分が作った staging、memory、retirement 登録を所有する。渡された application resource の所有は caller に残る。提出前の失敗では未提出記録を先に破棄してから一時資源を解放する。

arena／pool の操作は caller が直列化し、借用 backend に必要な実行 context 上で呼ぶ。Browser では JavaScript thread で生成・破棄する。Dispose は貸出が一つでも残る場合、何も破棄せず失敗する。caller が貸出を返してから再実行できる。Trim は未使用分だけを対象とする。

未使用 block／resource の破棄は、再利用候補から外してから全対象を一度ずつ試みる。一つの Destroy が失敗しても残りの Destroy を試み、元の例外を保持する。native 側で破棄が行われたか不明な object を cache に戻したり自動再試行したりしない。utility の Dispose が借用 backend を破棄することはない。

受理後は completion まで一時資源と明示的に引き受けた保持を維持する。例外、cancellation、token の破棄は GPU 完了を意味しない。通常 completion を保証できない device loss では利用を停止し、GPU の停止が確定するまで memory を再利用しない。

## コード配置

以下は repository root からの相対パスによる目標配置である。両 Resources project と隣接する xUnit project、Arena／Pool は実装済みで、Upload／Retirement とその試験は新設予定とする。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native.Resources/Utilities/Arena/` | `GpuMemoryArena`、`GpuMemorySlice` と統一 heap の block・範囲管理。 |
| `src/graphics/Lumyte.Graphics.Portable.Resources/Utilities/Pool/` | `GpuBufferPool`、`GpuTexturePool`、貸出 identity の `GpuBufferLease`／`GpuTextureLease` と description ごとの object 再利用。heap の取得・配置処理は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Utilities/Upload/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Utilities/Upload/` | 各系統の `GpuTransferUtilities`、staging、copy footprint と同期入力。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Utilities/Retirement/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Utilities/Retirement/` | 各系統の `GpuSubmissionToken`、`GpuRetirementQueue`、`GpuSubmissionException` と completion に従う保持・回収。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Tests/Unit/Utilities/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Tests/Unit/Utilities/` | production project に隣接する xUnit project。fake backend による範囲演算、pool と失敗時の所有の試験。completion／retirement の試験は後続。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Tests/Integration/Utilities/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Tests/Integration/Utilities/` | 同じ test project 内で分離する GPU 転送・完了の適合試験。必要な backend／hardware を明示する。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/`、`src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/BrowserHost/` | 既存 backend の fixture と GPU 排他を使う arena／pool の実機試験。Native の共用テスト source は二つの test project に link し、production の共通管理 interface は追加しない。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/Resources/` | 既存 benchmark project に追加する arena／pool／retirement の CPU 負荷と転送量の計測。 |

CPU の範囲演算や queue の helper は内部実装とし、共有を理由に共通 GPU 管理 API や共通 backend interface を追加しない。共通 facade の resource 対応表は各系統の新設 `src/graphics/Lumyte.Graphics.Native.RenderGraph/Resources/`／`src/graphics/Lumyte.Graphics.Portable.RenderGraph/Resources/` が所有し、ここへ移さない。この utility に shader source、生成 GPU 構造体、asset loader は置かない。

## 使用例

以下は Native utility の配置用領域を借りる例である。`requirements` は実際に配置する linear region に対して取得済みとし、GPU command はこの例では作成しない。

```csharp
using Lumyte.Graphics.Native.Resources;

using var arena = new GpuMemoryArena(nativeDevice, blockSize);
var slice = arena.Allocate(
    requirements.Size, requirements.Alignment, kind,
    [requirements.Compatibility]);
var region = nativeDevice.CreateLinearRegion(
    logicalSize, slice.Heap, slice.Offset);

nativeDevice.DestroyLinearRegion(region);
arena.Release(slice);
arena.Trim();
```

Portable 側は同じ native arena を経由せず、`GpuBufferPool`／`GpuTexturePool` を自身の backend に対して使用する。

```csharp
using Lumyte.Graphics.Portable;
using Lumyte.Graphics.Portable.Resources;

using var buffers = new GpuBufferPool(portableBackend);
var loan = buffers.Acquire(new GpuBufferDescription(
    4096, GpuBufferUsage.Storage | GpuBufferUsage.CopyDestination));
GpuBufferHandle buffer = loan.Handle;
// この例では GPU に提出しない。利用した場合は全利用終了を確認してから返す。
buffers.Release(loan);
buffers.Trim();
```

## 検証する契約

- arena の offset 算術、貸出と退役の区別、同じ取得済み要件の再利用。
- Portable pool の description ごとの再利用と、未完了 resource の再貸出し禁止。
- Native／Portable の token と resource が混在しないこと。
- 転送の CPU 範囲と await の正常完了時の GPU 完了。
- 受理前後の失敗、cancellation、複数 queue、device loss の保持。
- native validation の複製、command からの暗黙参照探索を行わないこと。

## 未実装事項

Native の統一 heap arena、Portable の Buffer／Texture pool、貸出 identity、Release／Trim／Dispose を実装した。これらは明示的な返却だけを扱い、raw command と GPU 利用の終了は caller が保証する。実機と単体試験の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。

GpuSubmissionToken、GpuRetirementQueue、Resources の GpuSubmissionException、Retire／Collect、GpuTransferUtilities と提出経路への接続は未実装である。下位には NativeGpuSubmissionException／Portable.GpuSubmissionException を実装し、queue への受渡し後・受理不明の同期失敗を要求した raw completion と結び付ける。これらは上位 token と別の型で、completion の到達を保証しない。通常 completion を確認できない障害からの停止確認・drain と、発行元が保証する利用終了を整えて上位へ接続する。command の状態照会、無効 token を完了扱いする処理、device loss 例外だけを根拠にした回収は追加しない。共通 backend adapter、互換 wrapper、旧 utility API の維持は対象に含めない。
