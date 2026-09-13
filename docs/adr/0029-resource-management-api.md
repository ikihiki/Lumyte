# ADR 0029: Native／Portable の Resource 管理 API

## 状態

採用（目標設計）。

`Lumyte.Graphics.Native.Resources` と `Lumyte.Graphics.Portable.Resources` を独立したライブラリとして定義する。資源の作成・所有・使用・回収には同じ API 名と意味を用い、管理する resource、shader 入力、配置と descriptor／binding の実装はそれぞれの系統に合わせる。

本 ADR の管理層は未実装である。ADR 0028 の明示的な arena／pool utility の完成とは分け、提出、completion、依存を伴う寿命管理と非同期 upload をここで定義する。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics 基礎](0001-graphics-api.md)・[0002 Native API](0002-native-graphics-api.md) | 二系統の境界、caller lifetime、native validation |
| [0003 Native Allocation](0003-native-memory-allocation-api.md)・[0004 Native Linear Data](0004-native-linear-data-api.md)・[0005 Native Texture](0005-native-texture-api.md) | 統一 heap と配置 |
| [0006 Native View](0006-native-view-api.md)・[0007 Native Bindless](0007-native-bindless-api.md)・[0008 Native Descriptor](0008-native-descriptor-api.md) | view、index、descriptor storage |
| [0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md)・[0015 Native Shader Package](0015-native-shader-package-api.md) | completion、Native 用 package と入力 metadata |
| [0016 Portable API](0016-portable-api.md)・[0017 Portable Resource のメモリ所有](0017-resource-memory-model.md) | 独立した Portable backend と resource 単位の生成・解放 |
| [0018 Portable Buffer](0018-buffer-api.md)・[0019 Portable Texture](0019-texture-api.md)・[0020 Portable View](0020-view-api.md) | device-owned resource と view |
| [0021 Portable Binding Layout](0021-binding-layout-api.md)・[0022 Portable Binding](0022-binding-api.md)・[0023 Portable Shader](0023-shader-design-and-api.md) | 明示 binding、Portable 用 package と入力 metadata |
| [0026 Portable 提出と同期](0026-command-submission-and-synchronization.md) | raw 提出、GPU 利用終了と診断、受理不明の失敗 |
| [0028 Resource Utilities](0028-resource-utilities.md) | 明示的な arena／pool の貸出・返却・未使用分の解放 |

## 決定

管理層は明示的に預かった資源の寿命を引き受ける。下位 API はこのライブラリを利用せず直接使用できる。Native manager を Portable backend で動かしたり、Portable manager を Native API の adapter として使用したりしない。

同名の `GpuResourceManager`、`GpuResourceScope`、`GpuResourceBatch` と managed reference を両 namespace に置く。C# の型としては別であり、相互変換や共通 `IGpuBackend` を設けない。この低層管理 API を直接使うコードは系統ごとの description、shader、入力型を選び、所有の操作を同じ形で記述する。

共通 RenderGraph を使うアプリケーションやライブラリには、これらの別型を公開しない。通常の Model、Blur、2D 等は機能 pass の CPU 入力と論理 I/O を渡し、選択された系統の pass 本体がこの manager を使って必要な資源を準備する。上位の `IGpuGraphResources` は準備済み upload data を共有 GPU package にする scope、upload、pin と回収を提供する。provider は runtime と世代の identity を持つ `GpuGraphBufferRef`／`GpuGraphTextureRef` 等の共通 export を自系統の managed reference に対応付ける。共通 facade に raw resource 生成、view／binding 操作や shader 用の data schema を公開しない。管理層は共通 Graph の interface を実装せず、共通型の wrapper と対応表は provider が所有する。共通契約の位置付けは ADR 0001 に従う。

Native は descriptor slot、linear region、texture の配置を管理する。Portable は実際に使用する view、sampler、binding set と device-owned resource を管理する。Bindless は Portable の要件ではなく、global descriptor index や全 slot 候補の模倣を要求しない。

下位 utility は `Allocate`／`Acquire`、`Release`、`Trim`、`Dispose` による明示的な貸出・返却だけを扱う。配置 resource、view、descriptor、binding の依存関係、提出と完了、staging の寿命は manager と uploader が引き受ける。utility に `Retire`／`Collect`、completion token、待機、upload を追加しない。manager は必要な object の利用と破棄を終えてから utility の `Release` を呼ぶ。

## API

### 両系統で揃える操作

表内の型はそれぞれの namespace に存在する別の型とする。この表は二つの低層管理 library の操作を揃える契約であり、共通 RenderGraph facade の公開型を定義するものではない。

| API | 説明 |
| --- | --- |
| `GpuResourceManager(device, options)` | 同じ系統の device/backend を借用し、resource record、pool、descriptor または binding cache、retirement を所有する。`options` の配置・容量設定は系統ごとに定義する。 |
| `CreateScope()` | caller が所有する空の `GpuResourceScope` を作る。scope は生成物に対する CPU 側の保持集合となる。 |
| `scope.CreateBuffer(description)` | 管理された `GpuBufferRef` を返す。Native では linear region/range を確保し、Portable では Buffer を確保する。 |
| `scope.CreateTexture(description)` | 管理された `GpuTextureRef` を返す。用途として指定された default view と、系統ごとに必要な準備を済ませる。 |
| `scope.GetView(resource, description)` | 指定した view の `GpuViewRef` を取得し、scope が保持する。同じ resource 世代と記述の view を再利用できる。 |
| `scope.GetSampler(description)` | 同じ系統の sampler description から準備済みの `GpuSamplerRef` を取得し、scope が保持する。同じ不変記述の sampler を共有できる。 |
| `GetBufferRange(reference, offset, length)` | 準備済み buffer の指定 byte 範囲を、Native では `NativeGpuRange`、Portable では `GpuBufferRange` として返す。保持を追加しない。 |
| `GetTextureHandle(reference)` | 準備済み texture の raw handle を同じ系統の型で返す。保持を追加しない。 |
| `GetTextureView(reference)` | 準備済み texture view を Native では `NativeGpuTextureView`、Portable では `GpuTextureView` として返す。新たな view 生成や保持追加は行わない。 |
| `scope.Release(reference)` | この scope の保持を放す。他の scope、pin、GPU 使用による保持には影響しない。 |
| `scope.ImportPackageAsync(plan, cancellationToken)` | 同じ系統の package plan を既定 pool で具体化し、view・shader 用参照・upload を構築する。初期 upload の GPU 使用終了と診断を含む成功確認後に `GpuPackageRef` を返す。Portable は配置指定を受け取らない。 |
| `scope.Dispose()` | 全 CPU 保持を放し、以後の生成を終了する。既存の pin と batch の使用保持は残る。 |
| `Pin(reference)` | scope 外で依存を含めて保持する `GpuResourcePin` を返す。 |
| `GpuResourcePin.Dispose()` | この pin の保持だけを放す。GPU 待機は行わない。 |
| `AcquireUse(reference)` | raw command と接続するため、依存を含む明示的な `GpuResourceUse` を取得する。記録前に呼ぶ。 |
| `GpuResourceUse.Dispose()` | caller が全未提出参照と GPU 利用の終了を保証して保持を返す。 |
| `GpuResourceUse.Retire(completion)` | 同じ manager が発行した有効な `GpuSubmissionToken` が全利用を覆うことを caller が保証し、保持の返却を manager へ移す。以後 caller はこの use を再利用・返却しない。 |
| `BeginBatch()` | 一回の提出と使用保持を管理する `GpuResourceBatch` を作る。 |
| `Collect()` | 明示保持を失った record と完了済み退役を非 block で回収する。 |
| `Trim()` | 完全に未使用の pool block または cached resource を解放する。使用中の資産を追い出さない。 |
| `WaitIdleAsync(cancellationToken)` | 呼出し時点でこの manager が管理する提出と退役を非同期に drain する。外部の手動提出を含む device idle は保証しない。取消しや観測失敗で保持を放棄しない。 |
| `Statistics` | 自分が管理する確保・使用・退役待ち・再利用量を返す。Native の実 allocation 量、Portable の把握できる resource 量と推定量を区別する。descriptor 数と binding cache 数などの詳細も系統ごとに定義する。 |
| `DisposeAsync()` | 管理する work を非同期に drain して pool と record を破棄する。外部の scope/pin/use は caller が先に返す。利用終了を確認できない場合は保持を残して失敗し、借用 device/backend を破棄しない。 |

`GpuBufferRef`、`GpuTextureRef`、`GpuViewRef`、`GpuSamplerRef`、`GpuPackageRef` は manager と record 世代を示す非所有値とする。変数へコピーしても保持は増えない。scope、pin、batch のいずれかで寿命を覆う。古い reference が再利用済み record へ接続しないよう、管理層が自身の世代を確認する。

default view と resource は一つの owner record にまとめる。default view からの参照を独立した所有 edge として resource に戻さず、相互保持の循環を作らない。追加 view や binding は参照先 resource の owner record を保持する。基本実装の通常回収に任意 graph の tracing GC を必須にしない。

`GpuPackageRef.GetExport(id)` は同じ系統の typed resource または package export を返す。export の `Pin`／`Use` は対象 resource と明示依存を保持する。Native の単一 allocation に属する export は、その allocation group 全体も保持する。両系統で揃える所有操作に native address、descriptor index、Portable binding layout を必須引数として加えない。

Native 管理層は `GpuBufferDescription(Size, MemoryKind, Alignment)` を定義する。`Size` は論理 byte 数、`MemoryKind` は確保用途、`Alignment` は論理範囲の要求整列値であり、Portable の Buffer usage flags を Native に要求しない。`GpuBufferRef` は大きな linear region 内の部分範囲を保持できる。Native の `GetBufferRange` は保持範囲の `Offset + offset` を用い、region の address に含まれる heap 配置 offset を再加算しない。

Portable の description と raw handle は Portable API の型を使う。raw 値の取得は管理権限の移管ではなく、caller は使用中の scope/pin/use を維持し、管理対象を下位 API から直接破棄・返却しない。

### Native 専用の shader 接続

| API | 説明 |
| --- | --- |
| `GetShaderIndex(view)` | 作成済み view 世代の descriptor index を返す。取得時の slot 割当・書込みは行わない。 |
| `GetShaderIndex(sampler)` | `GetSampler` で準備した sampler 世代の index を返す。resource の index 空間とは別であり、取得時に割当を行わない。 |
| `GetGpuAddress(buffer)` | 管理する linear range の address を返す。値自体には所有権がない。 |
| `GetRenderViewHandle(view)` | attachment 用に準備済みの `NativeGpuRenderViewHandle` を返す。生成や保持追加は行わない。 |
| `ResourceDescriptorHeap / SamplerDescriptorHeap` | manager が所有する二種類の `NativeGpuDescriptorHeap` を非所有で返す。raw command の手動記録へ設定するために使う。 |

descriptor allocator と storage は管理層が所有する。shader から参照する用途の view を作る時点で slot を割り当てて書き込み、利用者へ公開する。default view 以外は `GetView` の明示要求に応じて作る。view 世代の生存中は index を安定させ、使用中 slot の上書きや早期再利用を行わない。

Native 管理層の `GpuTextureViewDescription` は dimension、format、aspect、mip/layer 範囲に加え、`Purpose` と `RenderFlags` を持つ。`Purpose` は `GpuTextureViewPurpose` の `Sampled`／`Storage`／`Attachment` の一つである。shader 用途は対応する descriptor を、attachment 用途は `CreateRenderView` による handle を `GetView` の時点で作る。`RenderFlags` は attachment 用の `NativeGpuRenderViewFlags` とし、他の用途では `None` を指定する。shader index と attachment handle が必要なら、それぞれの用途の view を明示する。

Native の `GpuBufferViewDescription(Offset, Length, Access)` は管理 buffer 内の論理範囲と `NativeGpuBufferAccess` を指定する。`GetView` はその range を `WriteBufferDescriptor` に渡して descriptor を準備する。この経路は `BufferDescriptors` に対応する backend で利用する。`GetSampler` は sampler heap の slot を割り当てて `WriteSamplerDescriptor` を行い、管理された sampler 世代を公開する。

`NativeShaderLoader.Load(nativePackage)` が返す `NativeShaderProgram` と Native 用生成構造体を使用する。package builder が宣言した resource 関係から descriptor index と address を解決し、必要な通常の GPU data を準備する。共通 RenderGraph から使用する場合は Native の機能 pass 本体が準備済み shader package からの program 初期化・GPU cache とこの接続を担当し、consumer は Native の入力型を参照しない。buffer 内に格納した pointer/index の参照先は、package または caller が明示的に依存へ登録する。

### Portable 専用の shader 接続

| API | 説明 |
| --- | --- |
| `scope.GetBindings(program, group, inputs)` | `PortableShaderProgram` の指定 group と Portable 用の型付き入力から一つの `GpuBindingsRef` を取得する。program の binding metadata に従い、実際に指定された resource、view、sampler、range と dynamic offset 条件で immutable binding set を生成・再利用する。 |
| `GpuBindingsRef` | binding set 世代の非所有参照。scope が保持し、`Pin`／`Use` は binding set が明示参照する resource 世代も保持する。 |
| `GetBindingsHandle(reference)` | 準備済みの binding set の raw `GpuBindingsHandle` を返す。取得時に生成・更新・保持追加は行わない。 |

`PortableShaderLoader.Load(portablePackage)` と独立した入力構造体を使用する。Native の shader package、root 構造体、descriptor index table を変換して Portable 入力へ使わない。共通 RenderGraph から使用する場合は Portable の機能 pass 本体が、自身の shader、生成 GPU 構造体とこの binding 操作を使う。pass の共通 CPU 入力から共通 shader ABI への変換は要求しない。binding cache の再利用 key には layout と各 resource/view 世代を含め、古い世代が同じ数値 handle の新資源へ入れ替わらないようにする。

shader compiler は下位の POD 構造体と binding schema を出力し、Resources 向けの生成器がその schema から managed reference を受け取る入力型を別途作る。shader package／loader 自体を Resources や上位の実行器の型へ依存させない。Native の managed resource 参照を解決する入力生成も同じ依存方向に従う。

Portable の managed binding 入力の sampler は `GpuSamplerRef` を受け取り、準備済み description/object とその世代へ解決する。binding set の使用保持には sampler の依存も含める。

未使用 binding cache の entry は回収可能とし、cache 自体が参照 resource を永久保持する root にならないようにする。使用中 entry はその resource 依存を保持する。binding への登録は GPU の read/write や pass の同期宣言の代わりではない。

program とその layout は借用する。caller は参照する binding と GPU 利用が終了するまで program を保持する。未使用 binding cache は `Collect`／`Trim` で解放でき、shader program の明示破棄を妨げる永続 cache にしない。

### Batch

| API | 説明 |
| --- | --- |
| `batch.Use(reference)` | 同じ manager の resource、view、package、系統固有 binding などの明示依存を記録前から保持する。所有権は移さない。 |
| `batch.Use(scope)` | 呼出し時点で scope が保持する資源と依存を使用保持する。以後その scope に追加した資源を自動的に含めない。 |
| `batch.Own(scope)` | caller から scope の所有を引き受け、その時点の保持集合を固定する。以後 caller は生成・変更・終了しない。batch の終了要求と、提出した場合の completion 後に CPU 保持を返す。取得済みの他の pin/use は引き続き生存を保証する。 |
| `batch.Retain(lease)` | caller が明示的に渡した外部 `IDisposable` lease の返却責任を引き受ける。 |
| `batch.StartCommandRecording()` | この batch が所有・提出する、系統ごとの command を作る。Native は manager 自身の resource／sampler descriptor heap を初期設定する。 |
| `batch.Submit()` | 自身の全記録を一度だけ提出し、系統ごとの `GpuSubmissionToken` を返す。GPU 完了を待たず、実際の提出時に必要な PSO を準備する。受理不明を含む受渡し後の失敗では、同じ token を Resources の `GpuSubmissionException.Completion` に保持する。 |
| `batch.Dispose()` | 確実に未提出なら記録を破棄してから保持を返す。提出済み・受理不明なら、利用終了を確認できるまで記録に必要な保持を manager に残す。 |

`Use`／`Own`／`Retain` は対象を参照する最初の記録より前に行う。提出を開始した後は記録や保持対象を追加しない。batch は GPU state、shader の Parameter Data、CPU/GPU 間の競合を推論しない。command の root data は caller が渡した内容を直接送る。

Native の raw 手動記録では caller が公開された二つの descriptor heap を command に設定し、`AcquireUse` で必要な資源・sampler の使用を保持する。heap は manager が所有し、caller は manager の slot を手動変更・返却しない。

管理された reference を上位の実行器へ import する操作は、その実行に必要な使用保持を明示的に委譲する入口にできる。計画を compile しただけでは資源を所有しない。提出時の `Use` が成立するまで caller の scope または pin を有効に保つ。raw resource の import は caller lifetime に従う。

### Submission token と完了観測

表内の型も Native／Portable の各 Resources namespace に独立して定義する。token 発行と観測は manager の提出担当が所有し、arena／pool や公開された汎用 retirement utility の責務にしない。

| API | 説明 |
| --- | --- |
| `GpuSubmissionToken` | manager の発行元と提出 identity を表す非所有値。受理済みだけでなく、受渡しを開始して受理不明になった提出も識別する。public constructor に raw fence／semaphore を渡して作ることはできない。 |
| `IsValid` | 発行元が管理する提出に結び付くかを返す。default は無効であり、有効であることは受理・GPU 利用終了・処理成功の証明ではない。 |
| `IsComplete` | 発行元が当該提出の GPU 利用終了を確認できたかを返す。無効 token は false とし、利用終了を確認できない障害を完了扱いしない。 |
| `WaitAsync(cancellationToken)` | 当該提出の GPU 利用終了と診断を含む成功を非同期に待つ。取消しはこの CPU 待機だけを終了し、提出や保持を取り消さない。無効 token の待機は拒否する。 |
| `Equals / GetHashCode / == / !=` | 発行元と提出 identity の両方で比較する。同じ数値でも異なる発行元の提出を同一視しない。 |
| `GpuSubmissionException.Completion / InnerException` | manager の受渡し後・受理不明の同期失敗を、当該 token と元の障害へ結び付ける。例外自体は資源を所有せず、token の完了も保証しない。下位の raw completion を持つ例外とは別型である。 |

manager は提出 identity、観測先、一時資源と依存の保持を raw queue への受渡し前に準備する。正常な提出では token を返し、下位の `NativeGpuSubmissionException`／`Portable.GpuSubmissionException` を受けた場合は同じ identity の token を上位の失敗へ結び付ける。確実に未提出の失敗だけは保持を取り消せる。Native の未提出保証を持つ失敗結果は ADR 0012 の契約に従う。任意の別の host 例外だけを未提出の証拠にせず、受理不明の記録を再提出しない。

上位例外の `InnerException` には下位提出例外が保持する元の原因を引き継ぎ、診断を失わせない。発行元の raw timeline を含む下位提出例外そのものは manager 内部に保持し、上位の例外連鎖から signal 権限を公開しない。利用者が受け取る提出の識別は Resources の token に統一する。

発行元は同じ manager と借用 device に属し、提出に対応する completion の通知権限を管理する。Native では caller が `SignalCpu` で進められる任意の semaphore 値を回収の証明にしない。manager が非公開で所有する timeline と制御された signal を使用し、外部の raw fence を token に変換する API は設けない。Portable でも任意の数値を発行済み token として扱わず、管理する queue の提出と観測へ結び付ける。複数 queue の利用は、対象のすべての利用を覆う completion 群、または明示的に同期した最終 completion で保持する。

raw command を直接提出した caller は、必要な全利用の終了を自分で確認して `GpuResourceUse.Dispose()` を呼べる。manager が発行した token で全利用を覆える場合だけ `Retire` を使う。raw handle、address、descriptor index の取得から使用関係を自動推論しない。

GPU 利用終了と処理成功は別に管理する。`Collect` は前者だけを回収条件とし、upload 結果の公開は `WaitAsync` の成功を条件とする。Portable の `GpuExecutionException` は当該提出の診断を保って待機へ伝える。失敗した提出でも利用終了を確認できれば保持を回収できるが、内容が完成したとは扱わない。device loss、観測の失敗、取消しで利用終了を確認できない場合は保持を継続する。

Native の非同期観測も manager の提出担当へ接続する。下位の `WaitCpu` を呼出し元の thread で実行して非同期 API に見せかけず、UI や Browser の event loop を blocking wait で止めない。backend 固有の完了通知を利用する場合は公開された拡張契約を使い、production 間の `InternalsVisibleTo` を要求しない。観測機構は未実装であり、停止が確認できない障害からの drain を既存の raw Submit だけで保証しない。

### 内部 retirement と終了

`GpuRetirementQueue` 相当の登録・回収は manager 内部の helper とする。独立した公開 `Retire(completion, release)` utility は追加せず、`GpuResourceUse.Retire`、batch、scope と uploader の保持を manager の `Collect`／`WaitIdleAsync`／`DisposeAsync` へ接続する。

`Collect` は待機せず、利用終了を確認できた独立した登録を処理する。未完了の登録が先にあっても、依存しない完了済み登録の回収を妨げない。object の依存順序は manager が明示的に組み立て、同じ token に別々の callback を登録した順番だけを破棄順序の保証にしない。一つの回収が失敗しても独立した対象の処理を続け、元の障害を保持する。途中まで実行された破棄を自動再試行せず、破棄が不明な resource や配置範囲を再利用候補へ戻さない。

`WaitIdleAsync` は対象にした管理 work と必要な回収だけを drain し、外部 scope/pin/use を強制的に返したり device 全体の idle を保証したりしない。取消しや観測失敗でも登録と保持は manager に残る。`DisposeAsync` は外部保持が残る場合、何も破棄せず失敗する。終了を開始した後は新たな生成・提出を受け付けず、正常に drain できたものから依存順に破棄する。失敗しても未解消の保持と障害を残し、終了済みと偽らない。

backend の `Dispose`、raw `device.destroy()` の復帰、device loss の通知は GPU 停止の証明ではない。通常 completion を確認できない保持を解消するには別途確定した利用終了が必要であり、その公開契約と接続は後続の実装事項とする。manager の終了失敗を理由に借用 backend を破棄したり、utility の未返却 loan を強制回収したりしない。

## 配置と package

Native の既定 pool は linear、texture、attachment、upload/readback、package の用途ごとに管理する。確保 API は `NativeGpuHeap` に統一したまま、用途に適した配置・再利用方針を選ぶ。完全な description と取得済み requirement の組を用い、opaque compatibility を独自に分類しない。

Portable は Buffer／Texture の description、usage、転送方法に適した object pool と binding cache を使用する。必要なメモリは `CreateBuffer`／`CreateTexture` が確保し、pool の未使用 resource を破棄する際は対応する `Destroy` が解放する。manager は事前に heap を作らず、requirement の取得や配置 offset の計算を行わない。

両系統に同名の `GpuPackagePlan` を置き、resource description、準備済みの初期データ、明示依存、export を持たせる。plan は byte 列と記述を所有または移譲・コピーにより保持し、file path、URI、stream、外部 asset の解決用 ID を入力にしない。shader 入力と payload は別とする。配置 group と `GpuPackagePlacement` は Native 専用である。Portable の plan に heap、配置 offset、単一 allocation の要求を含めない。glTF decoder は graphics 管理層に含めない。

通常の機能 pass は request 内の準備済み CPU upload data を受け取り、`BuildAsync` で専用 GPU 配置・payload の準備・転送と使用保持を行う。ファイル取得、glTF・画像・font の解釈、shader package の読取りは `Lumyte.Resources` の分野とし、pass や manager にロード API を設けない。consumer に shader 管理や GPU plan の作成を要求しない。

pass 間で共有する画像等は、共通 facade の `scope.ImportPackageAsync(data, cancellationToken)` へ準備済み package upload data を渡す。provider の uploader がその用途契約と export から生成前に完全な resource description を持つ自系統の plan を作り、ここで定義する `scope.ImportPackageAsync(plan, cancellationToken)` に GPU 確保と初期転送を依頼する。内容と世代を示す key をロード先として解釈しない。共通 scope が系統別 plan や Native の placement を受け取る必要はない。Native 固有の配置指定は pass 実装または専用経路に保持し、Portable に heap 作成を要求しない。

pass 固有の Parameter Data と asset の GPU 表現は、その系統の pass 実装が明示的に準備・upload する。この manager は生成済みの payload と明示依存を受け取り、共通 Graph の入力型へ依存しない。共通の shader/data schema を consumer に要求せず、Native の pointer/index が入った bytes と Portable の binding 用 data を無条件に共有しない。低層 command が root の意味を解釈して Parameter Data を補助する経路は追加しない。

Native では追加の `scope.ImportPackageAsync(plan, placement, cancellationToken)` overload で配置を指定できる。配置を省略した共通操作は `Pools` を選ぶ。

| Native の配置指定 | 意味 |
| --- | --- |
| `Pools` | Native の既定 pool を使い、一つの package 所有集合として扱う。 |
| `SingleAllocation` | 指定 group の線形 data／Texture を実際に一つの allocation に配置する。全 requirement を Native の統一 heap 確保へ渡す。 |

Native の `SingleAllocation` を複数の確保へ黙って置き換えない。staging、外部共有 resource、descriptor storage まで一つの allocation に含める指定ではない。Portable の package は resource と binding の所有集合として構築し、物理 allocation 数や配置位置を契約にしない。

GPU package builder／uploader が準備済みの初期データと明示参照を GPU 配置へ具体化する。外部 asset の検索や decode は行わない。通常の material buffer などを構築する処理を command に追加せず、Native shader は root data から必要な参照を導出し、Portable shader は専用の入力 ABI に従う。初期 upload の成功前の package を完成品として公開しない。Portable は queue 到達と error scope の解決順にかかわらず、[ADR 0026 の成功待機](0026-command-submission-and-synchronization.md) を通す。validation／pipeline の失敗では package と export を返さず、GPU 使用終了に従って部分生成物を回収する。

### 非同期 upload と staging

非同期転送は manager が所有する uploader の責務とし、公開入口は package の `scope.ImportPackageAsync` に接続する。buffer／texture の upload、必要な readback、staging の確保、mapping、copy 記録と completion 待機を utility へ分割して所有関係を失わせない。単独転送の公開操作が必要になった場合も、manager の明示使用保持と成功待機の契約上で定義し、raw device だけを受け取る別の所有機構は作らない。

uploader は CPU byte 列をコピーまたは明示的な所有移管で保持し、記録と GPU 利用が終わるまで staging と転送先を保持する。Native は linear region の mapping と caller／plan が明示した before/after state・依存を使う。Portable は自身の Buffer／Texture、copy、mapping を使い、WebGPU の resource transition を二重管理しない。どちらも上位の成功待機が終わるまで package や readback 結果を完成品として返さず、待機取消しで提出済み転送の保持を解除しない。

uploader は CPU の byte 範囲、pitch と overflow、所有する payload の配置計算を確認する。GPU の format、usage、offset alignment、resource state の合法性は native API と validation に委ねる。複数箇所で実際に再利用できる CPU data layout の計算だけを純粋な helper に切り出してよいが、その追加は utility の完成条件ではない。GPU object、依存保持、提出や待機を扱う helper は manager／uploader 側に置く。command に Parameter Data の算出や root data の buffer fallback を追加しない。

## 回収と更新

確定する基本契約は、scope／pin による CPU 保持と、upload／未提出・提出済み batch による使用保持が両方なくなった時点で回収できることである。`Collect` は GPU 完了を待たず、まだ必要な世代を次回に残す。

管理する依存は明示登録された関係だけとする。Native は bindless で到達できる資源を package または使用者が登録する。Portable は実際の binding set が保持する資源を登録し、未使用の global slot 候補を保持する契約を設けない。raw pointer、root data、GPU buffer 内の index を走査しない。

変更は新しい resource/view/binding 世代を作り、旧世代は既存利用の終了まで保持する。scope 全体を凍結せず、使用中の世代を固定する。同じ resource の pixel/byte 更新に必要な GPU 同期は caller または上位実行器の責務である。

回収は依存の参照側から行う。Native では GPU 利用終了後に descriptor の管理参照と slot を返し、render view、texture、linear region などの配置 object を破棄してから `GpuMemoryArena.Release(slice)` を呼ぶ。未参照 slot の物理内容は次の割当時に上書きし、下位 API に存在しない clear 操作を要求しない。配置 object の破棄が失敗した場合、その範囲を arena へ返さない。arena 自体は object の一覧や破棄順を知る必要がない。

Portable では binding set と view／sampler の依存保持、mapping、未提出参照と GPU 利用を解消してから `GpuBufferPool.Release(lease)`／`GpuTexturePool.Release(lease)` を呼ぶ。pool が所有する Buffer／Texture は manager が直接 `Destroy` せず、返却後の再利用または pool の `Trim`／`Dispose` に委ねる。binding cache が返却済み handle を新しい世代として再参照しないよう、依存を持つ cache entry の解放を先に終える。pool に binding/view の寿命追跡を追加しない。

Native package の単一 allocation group は一括回収し、一つの export が残れば group 全体が残る。Portable は対象 resource と明示された依存の使用が終了した単位で回収し、物理 allocation group を作らない。manager が所有する utility へ返す順序も、batch／package の明示依存として維持する。

任意の管理依存 graph に対する tracing GC、予算に基づく資産 cache eviction、CLR GC 連動は別の検討事項とする。基本の明示所有・使用契約だけでも安全に回収できる実装を先に作る。GC 連動を追加する場合も finalizer から GPU API を直接呼ばず、保持の終了を manager へ通知する。

## 失敗と検証の境界

生成・upload が確実に未提出のまま失敗した場合は、未提出記録を先に破棄して部分生成物を回収する。受理済み・受理不明の提出は例外や cancellation でも利用終了を確認できるまで保持する。device loss では GPU 停止が確定するまで slot や memory を再利用しない。

GPU 使用終了と処理成功は、本 ADR の token と [Native の提出契約](0012-native-command-submission-and-synchronization.md)／[Portable の提出契約](0026-command-submission-and-synchronization.md) に従って区別する。Collect／退役は前者、package upload の正常終了は後者を使う。manager は runtime の診断を伝え、独自の GPU validator や shader の意味検証を追加しない。

管理層が検証するのは自分の manager/record 世代、所有 token、割当・返却状態、CPU 側の範囲演算である。GPU の format、usage、binding layout、state、hardware 制約を native validation と重複して検証しない。

manager と所有する utility の操作は caller が直列化し、借用 backend が必要とする実行 context 上で行う。Browser の resource／binding の生成・破棄と回収 callback は JavaScript thread で実行する。非同期の観測から直接別 thread で GPU object を破棄せず、manager の実行 context へ戻して回収する。

## コード配置

以下は repository root からの相対パスによる目標配置である。両 Resources project は低層 arena／pool utility を実装済みで、同じ project 内に本 ADR の管理層を追加する。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native.Resources/Management/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Management/` | 系統別の `GpuResourceManager`、scope、batch、pin、use、managed reference、record 世代、options と statistics。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Management/Submission/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Management/Submission/` | 系統別の `GpuSubmissionToken`、Resources の `GpuSubmissionException`、manager 内部の発行元、raw 提出との結合、GPU 利用終了と診断の非同期観測。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Management/Retirement/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Management/Retirement/` | manager 内部の retirement 登録、依存順の回収、Collect／drain と障害時の保持。独立した公開 utility は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Upload/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Upload/` | manager が所有する系統別 uploader、CPU payload と staging の保持、buffer／texture 転送、mapping、提出と成功待機。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Packages/` | Native の `GpuPackagePlan`、export、`GpuPackagePlacement`、requirement を用いた配置計画と uploader への接続。 |
| `src/graphics/Lumyte.Graphics.Portable.Resources/Packages/` | Portable の `GpuPackagePlan`、export、device-owned resource と初期 upload の計画。heap／placement 型は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Descriptors/` | resource／sampler descriptor slot、view 世代、shader index、GPU address と管理入力の解決。 |
| `src/graphics/Lumyte.Graphics.Portable.Resources/Bindings/` | `GpuBindingsRef`、型付き管理入力の解決、view／sampler と immutable binding cache。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Generators/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Generators/` | 新設の build 用 project。shader schema から managed reference を受け取る系統別の入力型を生成する。公開の管理操作を追加するものではない。 |
| 各利用 project の `obj/<Configuration>/<TargetFramework>/Shaders/Native/Resources/` または `obj/<Configuration>/<TargetFramework>/Shaders/Portable/Resources/` | 管理入力の生成 C#。shader の下位 POD／artifact と出力先を分け、生成内容を手書き source と混在させない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources/Utilities/`、`src/graphics/Lumyte.Graphics.Portable.Resources/Utilities/` | ADR 0028 の薄い arena／pool だけを置く。manager は依存解消・配置 object の破棄を済ませてから明示的に返却する。token、retirement、upload は置かない。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Tests/Unit/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Tests/Unit/` | 隣接する既存 xUnit project に `Management/Submission/`、`Management/Retirement/`、`Upload/`、`Packages/` と `Descriptors/` または `Bindings/` を追加する。明示所有、世代、cache 回収、受理前後の失敗を fake で確認する。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Tests/Integration/Management/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Tests/Integration/Management/` | GPU を使う package upload、使用中世代の保持、descriptor／binding と回収の適合試験。高速な unit suite から分離する。 |
| `src/graphics/Lumyte.Graphics.Native.Resources.Generators.Tests/Unit/`、`src/graphics/Lumyte.Graphics.Portable.Resources.Generators.Tests/Unit/` | 生成器に隣接する新設 xUnit project。schema から生成した入力を consumer で compile・実行し、管理参照の解決を検証する。生成 source 全文の比較を主試験にしない。 |
| `benchmarks/Lumyte.Benchmarks/Graphics/Resources/` | 既存 benchmark project に追加する package 配置、record／descriptor／binding 再利用、回収の計測。 |

共通 `IGpuGraphResources` の実装、共通 export の wrapper と対応表は、新設の `src/graphics/Lumyte.Graphics.Native.RenderGraph/Resources/`／`src/graphics/Lumyte.Graphics.Portable.RenderGraph/Resources/` が所有する。manager を共通 Graph 型へ依存させない。GPU payload と shader source は各 pass 実装に置き、ファイル取得・glTF 等の decode は `Lumyte.Resources` の担当に残す。

管理入力の生成器は利用 project の build から呼び、shader runtime から管理層や生成器への参照を作らない。ここで生成する入力は管理された resource 参照を受け取る。Portable の内部 logical reference を受け取る graph 入力の生成は Portable RenderGraph 側の build 用 project が担当し、この生成器へ上位 Graph 依存を追加しない。

## 使用例

以下は Portable の低層管理 API を直接使う目標コードである。`portablePlan` は準備済み CPU data から Portable 用 uploader が作った package plan、`RecordModel` はその系統の shader・入力・command を使う caller の描画処理とする。共通 RenderGraph の consumer は機能 pass を追加し、選択された Portable pass 本体がこれらの呼出しを担当する。

```csharp
using Lumyte.Graphics.Portable.Resources;

await using var resources = new GpuResourceManager(portableBackend, options);
using var scope = resources.CreateScope();
var model = await scope.ImportPackageAsync(
    portablePlan, cancellationToken);

GpuSubmissionToken completion;
using (var batch = resources.BeginBatch())
{
    batch.Use(model);
    var commands = batch.StartCommandRecording();
    RecordModel(commands, model);
    completion = batch.Submit();
}

scope.Release(model);
await completion.WaitAsync(cancellationToken);
resources.Collect(); // 完了した分だけ回収する
```

低層 API を直接使う Native コードでは namespace、device、plan と描画処理を Native 用へ変え、同じ所有操作を使う。Native で統合配置が必要なら配置指定付き overload に `GpuPackagePlacement.SingleAllocation` を渡す。共通 RenderGraph を使う consumer にこのコード変更や再コンパイルは要求せず、機能 pass の独立した本体が系統の差を引き受ける。

## 未実装事項

両 Resources assembly と、ADR 0028 の Native arena／Portable Buffer・Texture pool の明示貸出・返却を実装した。薄い utility の担当範囲に completion、retirement、upload は含めない。

本 ADR の GpuResourceManager、scope／pin／use／batch、GpuSubmissionToken と発行元、上位の GpuSubmissionException、Native の非同期完了観測、内部 retirement、自動 descriptor／binding 管理、用途別の割当方針、package upload と上位実行器への接続は未実装である。通常 completion を確認できない障害からの停止確認・drain も、下位の raw 例外や backend Dispose だけでは成立しない。実装時に保持を引き受ける完了・停止の公開契約へ接続する。utility の完成を管理層の完成とは扱わない。

CPU data layout の helper は実際に再利用できる計算が生じた場合だけ追加し、utility 完了の残作業としない。GC の具体アルゴリズム、予算圧力と `Lumyte.Resources` の cache 回収の接続、CLR GC 連動は採用範囲に含めず、別の設計案で検討する。
