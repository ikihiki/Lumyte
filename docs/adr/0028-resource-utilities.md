# ADR 0028: Native／Portable の Resource Utilities

## 状態

採用。ここで定義する utility の機能は実装済み。

Native の arena、Portable の Buffer／Texture pool と明示的な貸出・返却に責務を限定する。resource の依存、提出と完了、遅延回収、非同期転送は上位管理層が担当する。これらを utility の後続 API として追加しない。

Native と Portable に独立した utility を設ける。Native は native heap の範囲、Portable は自身の Buffer／Texture object を貸し出す。両 utility が共通 backend interface を介して動くことは要求しない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics 基礎](0001-graphics-api.md) | 二系統の責務、caller lifetime、native validation |
| [0003 Native Memory Allocation](0003-native-memory-allocation-api.md)・[0004 Native Linear Data](0004-native-linear-data-api.md)・[0005 Native Texture](0005-native-texture-api.md) | 統一 heap、requirement、region と texture の配置 |
| [0016 Portable API](0016-portable-api.md)・[0017 Portable Resource のメモリ所有](0017-resource-memory-model.md) | Portable device とメモリ込みの resource 生成・解放 |
| [0018 Portable Buffer](0018-buffer-api.md)・[0019 Portable Texture](0019-texture-api.md) | 完全な description による resource の生成・解放 |

## 決定

`Lumyte.Graphics.Native.Resources` と `Lumyte.Graphics.Portable.Resources` にそれぞれ必要な機構を実装する。Utilities と上位の Management は同じ Resources assembly 内の責務区分とし、新しい中間 library は設けない。公開型は系統ごとに独立し、Native の memory を Portable に渡せない。共通化できる CPU の範囲演算は内部で共有してよい。

これらは低層を直接使う caller、上位管理層と各機能 pass 実装のための API である。共通 RenderGraph の consumer は機能 pass に CPU 入力と論理 I/O を渡し、この utility の resource や貸出型を受け取らない。utility 側には共通 Graph の型への依存を加えない。

utility は自分が所有する heap／resource と貸出 identity だけを管理する。resource の所有関係、shader の参照先、pass の依存を推論しない。[No Graphics API](https://github.com/sebbbi/NoGraphicsAPI) の allocation と上位 allocator の分離を Native に適用し、Portable には自身の resource model に適した pool を用いる。

上位は依存関係を明示的に保持し、全利用の終了と必要な依存 object の破棄を済ませてから `Release` を呼ぶ。utility に `Retire`／`Collect`、completion token、公開 retirement queue、staging や非同期転送の所有を持たせない。raw copy の同義 wrapper や、利用箇所のない CPU layout helper を完成条件として追加しない。

## API

### Native の memory arena

| API | 説明 |
| --- | --- |
| `GpuMemoryArena(nativeDevice, blockSize)` | Native device を借用し、純粋な `NativeGpuHeap` block とその貸出範囲を所有する。 |
| `Allocate(size, alignment, kind, compatibilities)` | 同じ取得済み requirement token の参照 identity の集合と memory kind に対応する pool から、整列した `GpuMemorySlice` を貸し出す。token の順序・重複は集合の意味を変えない。新規 block では集合内の全 token を `CreateGpuHeap` に渡す。 |
| `GpuMemorySlice.Heap / Offset / Size` | backing heap、heap 基準の offset、予約 byte 数を持つ貸出 identity。public constructor は持たず、同じ範囲の再貸出でも新しい object にする。region や texture の生成は caller が行う。 |
| `Release(slice)` | 全参照の終了と配置 resource の破棄を caller が保証した範囲を返す。 |
| `Trim()` | 完全に空いた heap block を解放する。 |
| `Dispose()` | 貸出がすべて返却された arena を破棄する。残る範囲の強制解放は行わない。 |

arena は texture と linear data に同じ heap 型を使う。用途別の配置方針は上位が決め、arena は memory kind と requirement token の集合で block を分類する。caller が mixed allocation を要求した場合は全 requirement token を一度の確保へ渡す。共通 memory の選択は Native backend に任せ、管理層で token の bit 表現を再解釈しない。

参照 identity は取得済み token を再利用するための key とし、token の `Equals` や分類値から native 適合性を推定しない。別の取得で返された token は、同じ native 条件に見えても別集合として扱う。caller が requirement を保持して再利用する。block は要求以上で要求 alignment の倍数となる整列条件を持つものだけを再利用し、各 slice の開始を独立に整列させる。未使用範囲は分割し、返却した隣接範囲を結合する。新規 block は blockSize と要求 Size の大きい方を要求 Alignment へ切り上げる。size と alignment は非ゼロとし、CPU の範囲計算の overflow を拒否する。計算自体は正の alignment を扱い、native が受け付ける alignment の制約を独自に増やさない。

placement に渡す予約量は Native が返した `requirements.Size`／`Alignment` に従う。混在配置の境界 padding は Native の requirement に含まれており、arena が resource 分類や Vulkan の granularity を照会して重ねて計算しない。新しい resource を配置した場合に、以前の alias の内容を引き継ぐとは保証しない。

arena の slice は native allocation の別名ではない。heap 全体を解放する権限は arena にあり、caller は slice を `DestroyGpuHeap` へ渡さない。配置には `slice.Offset + localOffset` を一度だけ加え、linear region 内の offset と混同しない。

### Portable の resource pool

| API | 説明 |
| --- | --- |
| `GpuBufferPool(backend)`／`GpuTexturePool(backend)` | `IPortableGpuBackend` を借用し、完全な resource description と用途に従って resource を所有・再利用する。 |
| `Acquire(description)` | 使用可能な同条件の resource を、貸出ごとに新しい `GpuBufferLease`／`GpuTextureLease` に入れて返す。なければ Portable の生成 API で作る。 |
| `GpuBufferLease.Handle / Description`／`GpuTextureLease.Handle / Description` | 借用する raw handle と完全な生成 description。lease は pool が発行し、コピーしても貸出を増やさない。Handle の取得は返却まで可能で、Description は返却後も不変 metadata として読める。 |
| `Release(lease)` | mapping、view／binding、未提出参照と GPU 利用が終了した当該貸出を返す。別 pool・返却済みの lease は拒否する。 |
| `Trim()` | 貸出されていない resource を破棄する。GPU 待機は行わない。 |
| `Dispose()` | 全貸出が返却された pool を破棄する。 |

Portable はメモリを含む Buffer／Texture object を貸出・再利用する。pool の補充には `CreateBuffer`／`CreateTexture`、未使用 object の解放には対応する `Destroy` を使う。独立した heap の作成や placement は経由しない。buffer 内の range を分けて使う場合も、それは一つの Buffer の byte 範囲であり、独立した memory allocation の貸出ではない。

pool が resource を再貸出ししても、内容や GPU state が生成直後へ戻ったとはみなさない。初期化、先行利用との同期、次の利用に必要な状態は caller が処理する。

再利用条件には Buffer の Size／Usage と、Texture の dimension、各寸法、mip/layer/sample 数、format、usage、MutableFormat をすべて含める。用途の包含関係から大きい resource を代用したり、descriptor を補正したりしない。値をそのまま backend に渡し、GPU の合法性は runtime の診断に委ねる。

raw handle 自体は再利用するため、`Release(handle)` では古い貸出と新しい貸出を区別できない。返却には lease identity を使用し、古い lease が同じ handle の新しい貸出を返せないようにする。lease に自動 Dispose／finalizer による返却は設けない。caller は保存済み raw handle も含む全利用を終了してから明示的に返す。

### 上位へ渡す責務

| 処理 | 上位が扱う理由と utility への接続 |
| --- | --- |
| scope／pin／use／batch の保持 | CPU 所有、未提出記録、GPU 使用を上位の resource record で結び付ける。utility は貸出を継続するだけである。 |
| descriptor／view／binding と世代管理 | resource との依存関係を上位が把握し、全利用と依存を終了してから返却する。shader pointer や index から参照を探索しない。 |
| 提出 token、複数 queue の完了と遅延回収 | 提出元が必要な全利用を覆う証拠を管理し、回収順序を決めたうえで `Release` する。utility は raw timeline の値から完了を推測しない。 |
| upload／readback と package 配置 | staging、転送先、command、結果公開の依存を上位 uploader が所有する。Native は arena の範囲、Portable は pool の object を借りる。 |

低層を直接使う caller は、管理層を経由せず同じ返却前提を自分で満たしてよい。自動管理を強制せず、utility が追跡できない関係を推測して補うこともしない。

## 所有権と失敗

Native arena は heap と空き範囲を所有し、そこに配置した region／texture は caller が所有する。Portable pool は Buffer／Texture を所有し、caller は lease を通じて借用する。どちらも貸出の返却可否を決めるための依存 graph を持たない。

`Release` は即時に再利用を許す所有境界である。Native は配置 resource とその descriptor／view 等の利用・保持を終え、resource を破棄してから slice を返す。Portable は mapping、view／binding、未提出記録と GPU 使用を終了してから lease を返す。raw handle のコピーや lease 変数の寿命だけでは、この前提を証明できない。utility が検証するのは自分の owner／貸出 identity、返却状態と CPU 範囲計算であり、GPU 利用の有無や native の合法性ではない。

arena／pool の操作は caller が直列化し、借用 backend に必要な実行 context 上で呼ぶ。Browser では JavaScript thread で生成・破棄する。Dispose は貸出が一つでも残る場合、何も破棄せず失敗する。caller が貸出を返してから再実行できる。Trim は未使用分だけを対象とする。

未使用 block／resource の破棄は、再利用候補から外してから全対象を一度ずつ試みる。一つの Destroy が失敗しても残りの Destroy を試み、元の例外を保持する。native 側で破棄が行われたか不明な object を cache に戻したり自動再試行したりしない。utility の Dispose が借用 backend を破棄することはない。

返却前提を満たせない間は上位が貸出を保持する。例外、待機の cancellation、device loss と backend の Dispose 自体は GPU 利用終了の証拠にしない。依存関係の扱いを上位へ移しても、停止未確認の resource を返せるようにはならない。utility に暗黙の GPU wait、強制返却、finalizer による回収を追加しない。

## コード配置

以下は repository root からの相対パスによる実装配置である。両 Resources project と隣接する xUnit project、Arena／Pool と backend 実機試験を実装済みとする。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native.Resources/Utilities/Arena/` | `GpuMemoryArena`、`GpuMemorySlice` と統一 heap の block・範囲管理。 |
| `src/graphics/Lumyte.Graphics.Portable.Resources/Utilities/Pool/` | `GpuBufferPool`、`GpuTexturePool`、貸出 identity の `GpuBufferLease`／`GpuTextureLease` と description ごとの object 再利用。heap の取得・配置処理は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Tests/Unit/Utilities/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Tests/Unit/Utilities/` | production project に隣接する xUnit project。fake backend による範囲演算、pool と失敗時の所有の試験。 |
| `src/graphics/Lumyte.Graphics.DirectX12.Tests/Integration/`、`src/graphics/Lumyte.Graphics.Vulkan.Tests/Integration/`、`src/graphics/Lumyte.Graphics.WebGPU.Tests/Integration/`、`src/graphics/Lumyte.Graphics.WebGPU.Browser.Tests/Integration/BrowserHost/` | 既存 backend の fixture と GPU 排他を使う arena／pool の実機試験。Native の共用テスト source は二つの test project に link し、production の共通管理 interface は追加しない。 |

CPU の範囲演算や pool の helper は内部実装とし、共有を理由に共通 GPU 管理 API や共通 backend interface を追加しない。backend 接続には公開 interface を使い、production 向けの `InternalsVisibleTo` は設けない。共通 facade の resource 対応表は各系統の新設 `src/graphics/Lumyte.Graphics.Native.RenderGraph/Resources/`／`src/graphics/Lumyte.Graphics.Portable.RenderGraph/Resources/` が所有し、ここへ移さない。この utility に shader source、生成 GPU 構造体、asset loader は置かない。

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

- arena の offset 算術、整列、断片化と結合、取得済み requirement token の参照集合による再利用。
- Portable pool の完全な description ごとの再利用と、貸出中の object の再貸出し禁止。
- owner／貸出 identity による別 pool と古い貸出の拒否。
- active loan がある Dispose の無変更拒否、Trim の未使用分だけの破棄、Destroy 失敗時の全対象の試行と再利用禁止。
- DX12／Vulkan での配置と範囲再利用、Dawn／Browser での object 再利用を、caller が GPU 利用終了を確認して行うこと。
- native validation の複製、command からの暗黙参照探索を行わないこと。

## 未実装事項

この ADR が担当する Native の統一 heap arena、Portable の Buffer／Texture pool、貸出 identity、Release／Trim／Dispose に未実装 API はない。実機と単体試験の範囲は [進捗記録](../designs/graphics-implementation-progress.md) に記載する。

resource manager、scope／pin／batch、提出 token、自動 descriptor／binding、遅延回収と uploader は上位管理層の未実装事項であり、utility の完成条件には含めない。utility 単体の性能 benchmark と全 adapter／format の適合性は確認しておらず、機能の実装完了と性能・網羅的な hardware 検証を区別する。共通 backend adapter、互換 wrapper、旧 utility API の維持は対象に含めない。
