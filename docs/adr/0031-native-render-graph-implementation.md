# ADR 0031: Native RenderGraph 実装

## 状態

採用（目標設計）。共通の機能 pass を DirectX 12／Vulkan 向けの Native pass 実装で実行する provider を定義する。現行実装の完了を示さない。

## 依存 ADR

- [0001 Graphics](0001-graphics-api.md): 共通契約と二つの実装系統の境界。
- [0002 Native Graphics](0002-native-graphics-api.md)〜[0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md): allocation、resource、descriptor、shader、command と completion。
- [0013 DirectX 12](0013-directx12-backend-implementation.md)／[0014 Vulkan](0014-vulkan-backend-implementation.md): Native backend の実装。
- [0015 Native Shader Package](0015-native-shader-package-api.md): Native artifact、入力 ABI と loader。
- [0028 Resource Utilities](0028-resource-utilities.md)／[0029 Resource Management](0029-resource-management-api.md): Native の転送、scope、pool、使用保持と回収。
- [0030 RenderGraph](0030-render-graph-api.md): 共通の機能 pass 契約、外部入出力、論理 plan、resource facade と実行契約。

## 決定

`Lumyte.Graphics.Native.RenderGraph` は共通の `IGpuRenderProvider` と `IGpuRenderRuntime` を実装する。共通 graph に追加された Model 描画、Blur、2D 描画などの機能 pass に対し、登録済みの Native pass 実装を選び、その実装が Native 専用の内部 graph と command を作る。

共通化するのは機能の入力・出力・効果と追加操作である。利用者は shader をロードせず、Draw／Dispatch の共通命令列も記録しない。Native pass の作者が shader、GPU 構造体、GPU uploader、material の配置、描画方式と内部 pass 分割を所有する。Portable の実装と shader 入力の変換だけを分ける設計にはしない。

同じ Blur 契約でも Native 実装は一つの compute pass にまとめてよく、別の系統が二つの pass を使ってもよい。Model 描画は Native の Bindless、線形 range、GPU address と descriptor storage を使って独自にまとめられる。外部に宣言した依存、結果、更新範囲と契約の精度を保つことが共通実行の条件となる。

## API

この package は provider を登録する host と、Native pass 本体を実装する作者のための API を提供する。機能 pass を使う application と library は ADR 0030 の共通契約だけを参照する。

### Provider と pass の登録

| API | 契約 |
| --- | --- |
| `NativeRenderBackendFactory(options, cancellationToken)` | 共通 runtime 設定から、新しく生成した `INativeGpuBackend` を `ValueTask` で返す delegate。device の所有を runtime へ渡す。 |
| `NativeRenderProvider(id, createBackend, passes)` | backend factory と `NativeRenderPassRegistry` を使う provider を作る。この時点で device や shader を生成しない。 |
| `Id`／`ContractVersion` | `IGpuRenderProvider` の member。provider 識別子と共通 RenderGraph 契約の version を返す。 |
| `CreateAsync(options, cancellationToken)` | backend、Native Resources と pass 実装を所有する runtime を生成する。戻り値は共通の `IGpuRenderRuntime` とする。 |
| `NativeRenderPassRegistry.Register<TRequest, TResult>(contract, factory)` | composition 用の bootstrap API。共通 `IGpuRenderPassContract<TRequest, TResult>` に対応する Native 本体の factory を登録する。契約の `Id`、`Version` と request/result 型が一致する実装を選ぶ。重複する実装は登録しない。 |
| `NativeRenderPassFactory<TRequest, TResult>(services)` | `NativePassServices` とあらかじめ注入した CPU 依存を借用し、新しい `INativeRenderPass<TRequest, TResult>` を返す同期 delegate。GPU 生成や I/O は行わない。型付き factory は AOT で直接登録できる。 |
| `INativeRenderPass<TRequest, TResult>.BuildAsync(context, request, result, cancellationToken)` | 固定 request、今回の不変入力と宣言済み result から、Native 専用の GPU 配置・転送・shader program と内部 pass を準備する。template と未変更部品を再利用できる。ファイルの取得・解釈は行わない。 |
| `INativeRenderPass<TRequest, TResult>.DisposeAsync()` | runtime がこの実装の構築と GPU 使用を終了した後、実装が所有する shader cache、scope とその他の保持を終了する。 |

registry は provider の作成時に不変の登録集合として取得する。factory は runtime ごとに pass instance を生成し、runtime 間で device object を共有しない。同一 instance の `BuildAsync` は runtime が直列化する。記録 callback が参照する実行 state は構築後に変更せず、次の構築とは別に保持する。

通常の起動では Native の Hosting integration が Generic Host の登録定義と Options を受け、runtime 単位の DI scope から CPU 依存を型付き factory へ渡す。登録集合を固定し、必要な shader package の準備を非同期に完了してから core provider を組み立てる。CPU 依存と package を受け取った closure は runtime の寿命だけ保持し、root singleton に runtime 専用依存や GPU を持つ pass を保存しない。既存 runtime の登録は実行中に書き換えない。

`NativePassServices` と BuildContext は DI container を公開しない。`IServiceProvider`、Options と Hosting への依存は integration 層に置き、pass の constructor へ型付きの CPU 依存を渡す。BuildAsync や各 frame から service を解決しない。DI の同期 factory は登録・生成方針だけを用意し、GPU program の初期化は専用の非同期準備へ残す。

DirectX 12 と Vulkan はそれぞれの backend factory を持つ provider として登録できる。Native の pass library は Native API に対して実装し、必要な最適化の選択はその内部で行う。application が別の pass 追加コードへ分岐することは要求しない。

pass library は必要な shader artifact と入力型を定めるが、パス・URI の解決、stream の読取り、画像・glTF・font の解釈、shader container のデシリアライズは `Lumyte.Resources` の分野とする。この package に asset source や loader の登録 API を追加しない。host から渡された shader package、固定 request と今回の bindings が保持する upload data だけで `BuildAsync` を実行できるようにする。

factory が正常に返した backend と pass は runtime が所有する。factory 内で失敗した部分生成物は factory が回収し、それより前に生成済みのものは runtime 作成処理が回収する。DI から借用した CPU 依存と専用サービスは pass が破棄しない。Hosting integration は runtime の終了後に runtime 単位の DI scope を終了し、同じ pass、manager、backend を DI の別の破棄対象として登録しない。外部 device の借用はこの入口に含めず、必要なら所有を明示する provider 専用 integration で扱う。

### Pass 本体のサービスと構築

| API | 契約 |
| --- | --- |
| `NativePassServices.Backend`／`Resources`／`ShaderLoader` | 同じ runtime の Native backend、Native manager と Native shader loader を借用する。loader は展開済み package から GPU 用 program を作る非 I/O 操作であり、資産 source を持たない。サービス自体を破棄しない。 |
| `NativePassBuildContext.Services` | この構築に対応する `NativePassServices` を返す。 |
| `GetInput(value)` | 固定 request の `GpuGraphValue<T>` を、今回の bindings または定数の不変 T に解決する。この pass が ReadInput で宣言した値だけを読める。 |
| `ImportBuffer(resource)`／`ImportTexture(resource)` | 宣言済みの logical resource を今回の `NativePassBuffer`／`NativePassTexture` に接続する。resource input は今回の ref、transient は今回の実行に解決する。 |
| `ImportBuffer(reference)`／`ImportTexture(reference)` | 実装が所有・保持する Native managed reference を内部 graph に取り込む。外部資産の依存は共通契約または package の明示依存で覆う。 |
| `CreateBuffer(name, description)`／`CreateTexture(name, description)` | Native description を持つ内部 transient を宣言する。heap の物理配置は後の計画で行う。 |
| `CreateView(name, resource, description)` | Native 用途・範囲を持つ `NativePassView` を宣言する。shader descriptor と attachment view を用途に応じて準備する。 |
| `AddPass<TState>(name, state, record)` | 不変の実行 state と `NativePassRecordAction<TState>` を登録し、`NativePassBuilder` を返す。一つの共通機能から複数回呼べる。 |
| `NativePassBuilder.Read(resource, usage)`／`Write(resource, usage)`／`ReadWrite(resource, usage)` | 内部 resource の先行内容の読取り／全範囲初期化／部分更新・保持を宣言する。 |
| `NativePassUsage(Stages, Access)` | Native の同期計画に必要な `GpuStage` と `GpuAccess` を指定する。共通の機能 pass 利用者には要求しない。 |
| `Retain(lease)` | この実行の記録と GPU 使用が終了するまで必要な `IDisposable` lease の返却責任を provider へ渡す。shader cache の使用 lease などに使う。返却は提出・内容生成の成功通知ではない。 |

`NativePassBuffer`、`NativePassTexture`、`NativePassView` は、この runtime の内部 graph の非所有参照である。`NativePassBuildContext` は一回の構築に限って使い、callback に保存しない。内部 pass の state は値の snapshot または実行専用の不変 object とし、caller の後続変更を読み直さない。

共通 `Declare` は外部の Read／Write／ReadWrite、ReadInput、固定データの ReadUpload と生成出力を宣言する。実際の shader stage、attachment、copy、barrier と一時資源は Native 本体が選ぶ。内部 graph は外部依存の間に展開し、実装が隠れた外部読取りや書込みを追加しない。private transient と実装専用の不変 shader 資産は外部入出力へ加える必要がないが、変更可能な共有 cache や外部 package の使用は依存と所有を明示する。

request、GetInput、所有情報を共有する不変 snapshot と UseDeclared は [ADR 0030](0030-render-graph-api.md) の「不変入力と使用保持」に従う。共通 resource description は extent、format、byte 数等の外部意味を示し、usage や shader stage を固定しない。Native 本体が内部使用を宣言し、provider が実際の用途をまとめて物理 description を具体化する。

### GPU 内容世代の登録と再利用

pass が frame をまたいで保持する GPU 内容は、CPU template と分けて次の SPI で管理する。provider が書込みの提出結果と使用保持を結び付けるための契約であり、低レベル command の状態公開や機能 pass 利用者の登録 API にはしない。

| API | 契約 |
| --- | --- |
| `NativePassContentGeneration<TContent>` | 同じ runtime の不変な GPU 内容世代を表す opaque ticket。TContent は pass 作者が定める不変の GPU 表現情報と managed ref であり、一回の Build に属する内部 graph 参照を保存しない。内容の直接取得や状態照会は提供しない。 |
| `NativePassBuildContext.RegisterContent(content, lease, writers)` | 今回の Build が登録した一つ以上の `NativePassBuilder` を、内容を生成する全 writer として結び付け、ticket を返す。正常復帰時に内容の資源を保持する lease の返却責任を provider へ移す。登録元 Build の使用保持も取得するが、他の Build への利用可能化は writer の受理まで行わない。 |
| `RegisterContent(content, lease, submission)` | 同じ runtime の Native Resources が発行した `GpuSubmissionToken` で、先行提出してよい private 初期化の受理済み書込みを登録する overload。provider は発行元の completion と結果を追跡する。未管理 raw 提出をこの入口で推測しない。 |
| `TryUseContent(generation, out content)` | ticket が利用可能なら、その世代の保持と書込みへの順序・結果依存を今回の Build に取得して TContent を返す。null、失効・破棄済み、または別の未受理 Build に属する ticket なら false を返し、保持も追加しない。別 runtime の ticket は契約違反として拒否する。 |
| `generation.Dispose()` | cache owner の保持と以後の新規取得を終了する。既に取得した実行や受理済み writer の保持は終了させない。GPU 完了待ち、書込み取消し、成功通知は行わない。 |

`writers` は登録時に固定する。作者は、その内容を作る初期化・更新の全 pass を含め、各 pass に実際の resource の Read／Write／ReadWrite を宣言する。ticket は使用宣言の代わりにならず、未使用 cache を生成するためだけに culling を無効化しない。必要な writer が一つでも culling された場合は ticket を失効させ、未実行の世代を pending のまま cache に残さない。登録元 Build では writer との内部依存を付けて内容を使用でき、後続の Build／記録／提出が失敗した場合は未受理 ticket を失効させる。

全 writer の受理と ticket の利用可能化は provider が一つの操作として確定する。一部の writer だけが受理された場合は、ticket を利用可能にせず、その work の使用保持だけを完了まで維持する。登録元の実行にも writer の結果依存を付ける。未完了の ticket の取得では、同じ main queue の提出順序と必要な Native barrier を引き継ぐ。別 queue の GPU wait を下位 API に追加せず、provider が管理していない queue の token は受け付けない。完了を観測するまで成功した内容とは扱わず、失敗した writer に依存する未提出 work は提出を中止し、受理済み work の completion と派生内容も成功にしない。失効と GPU 使用終了は別に処理する。

取得・失効・cache owner の返却は provider 内で直列化し、`TryUseContent` の成功直後に失敗が判明しても実行の保持を失わない。provider から pass instance の dictionary を変更する非同期 callback は呼ばない。pass は通常の cache key で ticket を保存し、false なら完全な入力から再構築して古い ticket を Dispose する。元の Build が失敗しても、別の private 初期化が既に受理済みなら、その token に登録した ticket は当該転送自身の結果に従う。

失効した ticket は cache owner の資源保持も終了要求するが、取得済み writer／reader の使用保持を残す。未提出側は Build／記録を停止し、その記録を破棄してから保持を返し、受理済み側は GPU 使用終了または確定した device 停止まで保持する。pass が失効済み ticket を保持し続けても GPU 資源の恒久 owner にはならない。TContent は非所有の実行入力として扱い、次の Build では以前返された値を直接流用せず TryUseContent を通す。内容の資源一覧と書込み範囲は作者の明示契約とし、provider が TContent の reflection や GPU bytes の走査から推測しない。

cache 内容世代の storage は不変とし、旧 ticket の取得可能期間または既存 reader の使用中に同じ bytes を新しい内容で上書きしない。差分更新は新領域へ必要な基底と差分を転送して新 ticket にするか、旧保持がすべて終了した pool／ring 領域を使う。作成した新内容の公開で旧 ticket の参照先を差し替えない。単一 execution 内の順序付き ReadWrite は引き続き使えるが、更新される storage を複数の不変 cache 世代として登録しない。

### Native の記録 callback

| API | 契約 |
| --- | --- |
| `NativePassRecordAction<TState>(context, state)` | 物理資源と使用保持が準備された後、内部 pass を記録する同期 delegate。 |
| `NativePassRecordContext.Commands` | provider が所有する `NativeGpuCommandBuffer` を借用する。作者は Native の pipeline、rendering、Draw／Dispatch／copy を直接記録する。 |
| `GetBufferRange(buffer, offset, length)` | 内部 buffer の範囲を `NativeGpuRange` に解決する。offset は論理範囲から一度だけ加算する。 |
| `GetTexture(texture)`／`GetTextureView(view)` | 準備済みの `NativeGpuTexture`／`NativeGpuTextureView` を非所有で返す。 |
| `GetShaderIndex(view)`／`GetRenderView(view)` | 用途に従って準備した descriptor index／`NativeGpuRenderViewHandle` を返す。取得時に生成や割当は行わない。 |

record context は callback 内だけで使う。pass が command の提出・破棄や queue の切替えを行わず、provider が内部 graph 全体の順序と提出を所有する。必要な Native descriptor heap は管理 batch が設定する。callback は resource や descriptor を初めて生成する場所ではない。

root は Native 用に生成した構造体から直接 bytes にし、Native command の引数に渡す。通常の material、Parameter Data、配列は pass 本体の GPU uploader が明示的に準備・upload し、shader が root の参照・index・offset から読む。command が不足 data を推測して生成する経路と、root を隠れた buffer へ移す fallback は設けない。

### 共通 runtime の実装

| 共通 API | Native 実装の担当 |
| --- | --- |
| `runtime.Resources` | 共通 scope、準備済み package の GPU upload、resource export、pin と回収を Native Resources に接続する。Native heap、address、descriptor index と shader の型を公開しない。 |
| `SubmitAsync(plan, bindings, cancellationToken)` | live な機能に対応する Native 本体を選び、内部計画と部品を再利用し、今回の入力と資源に必要な準備・記録・下位提出を行う。bindings 省略時は CPU 初期値を使う。queue の受理後に共通 execution を返す。 |
| `WaitIdleAsync(cancellationToken)` | runtime が管理する構築・提出・転送と退役を drain する。外部の未登録使用まで終了したことにはしない。 |
| `DisposeAsync()` | 新しい work の受理を終了し、実行を drain して pass、資源、pool と backend を順に終了する。 |

共通 ref は runtime identity と record 世代を持つ。異なる runtime の同じ数値 handle を代用しない。共通 facade の `ImportPackageAsync(data, cancellationToken)` は準備済み `GpuPackageUploadData` を Native の upload 計画へ接続し、GPU 確保と転送を行う。pass 用 shader のファイル取得・package 展開は `Lumyte.Resources` 側で済ませ、host が不変の Native shader package を pass factory の closure に渡す。pass 本体は GPU program と専用入力を管理し、`runtime.Programs` のような利用側の shader 管理窓口を設けない。

## 実行計画と最適化

共通 `Compile` は外部依存、入力 slot と機能契約を持つ論理 plan を作る。Native の shader、root bytes、heap と command は作らない。同じ plan を異なる bindings で繰り返し提出し、内部 graph の依存から今回の実行順、寿命、barrier と texture transition を具体化する。

Native 本体と provider は内部 graph の template、CPU 入力の所有情報、geometry／material の GPU 表現、batch、bounds 等を保持し、実際に依存する値や世代が変わった箇所を更新する。BuildAsync は構築と更新の入口であり、毎回すべての内部 node、upload 参照、pipeline 定義を作り直す要件ではない。template が有効なら今回の入力・資源・実行 state を結び付けて再利用する。差分情報が使えない初回、旧世代、cache eviction では完全な snapshot から再構築できる。

camera の変更による culling／透明 sort などは依存に応じて実行する。外部構造が同じまま内部の draw 数や一時資源が変化しても、共通 plan の再 Compile は不要である。command の再記録、bundle 等の再利用、CPU／GPU の分担は Native 内部の選択とし、同じ command buffer の再提出を共通要件にしない。

Native のメモリ取得は統一した `NativeGpuHeap` を使う。Native Resources が linear、texture、attachment、package 等の pool と配置を管理し、内部 graph は使用期間と再利用条件を渡す。Native pass は pipeline の部品、Bindless、linear range と native indirect 操作を利用できる。内部アルゴリズムの適合性は機能契約の結果で判断し、Portable と draw 数、workgroup、shader 数や一時資源数を一致させない。

mesh 経路は既存の record context から [ADR 0011 の DispatchMesh／DispatchMeshIndirect](0011-native-command-recording-api.md) を記録する。新しい共通 AddPass や専用の mesh graph を設けない。作者は MeshShaders／AmplificationShaders と対応 artifact を使い分け、内部 resource に MeshShader／AmplificationShader の使用、indirect 引数に DrawIndirect の読取りを宣言する。compute による meshlet・可視 list・引数の生成は先行する内部 pass として登録し、実際の依存を barrier へ接続する。mesh payload は shader 内の stage 間データであり、CPU root や command の Parameter Data 生成機能にはしない。

機能が vertex と mesh の両経路を持つ場合、選択は Native 本体の準備で行う。mesh command を下位 backend が他の命令へ自動変換する契約はない。mesh 用の派生 cache は元データ、packing・shader の版と device 条件へ結び付け、RegisterContent／TryUseContent で結果と使用保持を管理する。Slang 計算 module の共有も build 時に限り、Portable の入力型や内部 graph を読み替えない。

物理配置計画が異なる resource 間の alias 切替を行う場合は、provider が先行使用と memory dependency を順序付け、texture の再利用前に [ADR 0011](0011-native-command-recording-api.md) の `DiscardTexture(view, afterLayout)` を明示記録する。Vulkan では `General`、DirectX 12 では次の用途の layout を指定する。global Barrier だけで再初期化済みとは扱わず、未提出記録も成功扱いにしない。Native backend に自動 alias 検出や全資源の state tracker を追加しない。

persistent な package export は準備済み `GpuPackageUploadData` の `Profile` と export 契約で利用範囲を定め、対応する Native uploader が usage を含む完全な物理 description を生成前に package plan へ渡す。`Profile` は用途契約の識別子であり、ファイルや loader の解決先ではない。registry の factory や未実行の BuildAsync から用途を推測しない。再 import と後続機能はその生成契約の範囲で利用し、既存 texture の用途を暗黙に広げない。この生成・所有契約を Native API の合法性の独自検証へ広げない。

内部 graph の同期宣言を省略して GPU pointer から使用先を推論しない。Native の物理 cache は device、pass 実装の世代、shader artifact、resource 世代と requirement に束縛する。共通 plan の cache と、実体を所有する Resources／shader cache の役割を分け、同じ物理資源を二重に所有しない。

不足する raster PSO は実際に提出された draw の組合せに対し、下位の提出処理が生成する。pass の登録、共通 Compile、破棄される未提出記録だけのために実 PSO を生成しない。`PrepareRasterPipeline` を追加しない。pass が事前に保持する pipeline の論理記述と、提出時の実体化を区別する。

## 所有、非同期処理と失敗

入力の固定、await 前の使用保持、取消し、提出結果、runtime の終了順序は [ADR 0030](0030-render-graph-api.md) の「所有、同期、失敗」を正とする。Native pass は shader と通常の GPU data を自分の cache または scope で所有し、その実行で使う shader lease を Retain、GPU 内容を RegisterContent／TryUseContent に接続する。記録から GPU 使用終了まで program、pipeline、view、descriptor と参照資源を維持する。

shader program の GPU 初期化と通常 data の upload は異なる処理である。準備中に受理された転送は runtime が completion と staging を追跡し、その後の構築が失敗しても転送完了まで保持する。caller の CPU memory はその非同期操作の契約を超えて参照しない。初回 upload だけでなく更新も既存の使用と正しく順序付ける。

準備で先行提出してよい upload は、新しい private 資源・不変世代の初期化など、現在の plan の GPU work に先行できるものに限る。同じ plan の先行 pass が読む資源や共有 cache を更新する場合、BuildAsync は staging と lease を準備し、内部 AddPass の CopyMemory／CopyMemoryToTexture 等で更新と依存を記録する。staging の使用も内部 graph と Retain に登録し、CPU 準備の順番だけで GPU の先行利用を追い越さない。

共通機能に対応する Native 本体がない場合は、GPU 資源の準備を始める前に理由を返す。本体が要求する shader package、ABI、必要機能が揃わない場合は、その準備で失敗する。別の意味の処理や Portable 本体へ暗黙に置換しない。

GPU 内容の利用可能性は上記 ticket を provider が管理する。Retain の返却や CPU cache の準備完了から推測しない。device loss で ticket が失効しても、GPU 停止が確定するまでは memory と descriptor slot を回収しない。

外部 raw Native resource と command の接続は provider 専用 integration に置く。caller が既知の state、先行 completion と所有 lease を渡し、以後の排他的な管理範囲を明確にする。低レベル Native API に全資源の状態照会や外部利用の追跡を加えない。

resource input による presentation target の差替えは共通 helper と接続する。Acquire 済み target は今回の bindings にのみ結び付け、description が変わる場合は新しい共通 plan を要求する。GPU completion、target の再取得条件と host の frame pacing を分ける。

## 検証

Lumyte が確認するのは機能契約の Id/version と型、runtime identity、record 世代、package/ABI、内部 graph の明示依存、所有と CPU コピーの範囲である。Native shader、pipeline、descriptor、format、usage と barrier の合法性を再検証する validator は実装しない。下位 API、compiler、debug／validation の診断を保つ。

外部契約の意味を実装が守ることは pass 作者の責務であり、GPU 命令の解析から証明しない。同じ共通 request に対する結果、宣言した破壊範囲、依存と失敗契約を、pass library の適合試験で確認する。

内容世代の試験では、writer 登録後の別 pass の構築失敗、提出失敗、受理済み未完了世代の取得、失効と取得の競合、依存先失敗の伝播、owner の早期 Dispose と GPU 使用保持を fake completion で確認する。未提出 writer を次回に再準備でき、旧世代を保持した描画の bytes が新世代によって変更されないことを観測する。Native alias の実機試験では同じ領域の texture → 別 resource → texture を再初期化して使う。

## コード配置

repository root 相対の目標配置を次に示す。`Lumyte.Graphics.Native.RenderGraph` と隣接 `.Tests` は新設予定とし、共通 graph、Native API、Native Resources／Shaders に依存する。機能 pass 本体から利用する SPI と、provider の内部実装を同じ project 内で分ける。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/Registration/` | NativeRenderProvider、backend factory、NativeRenderPassRegistry と型付き pass factory。Host に依存しない bootstrap API |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/Passes/` | INativeRenderPass、NativePassServices、build／record context、内部 logical resource、使用宣言と NativePassContentGeneration の SPI |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/Planning/` | 内部 graph の順序、依存、barrier／配置計画、再利用する template と実行ごとの物理化 |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/Resources/` | 共通 facade の Native 実装、共通 ref と managed ref の対応、upload profile の具体化、transient と使用保持 |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph/Execution/` | 共通 runtime の実装、command 記録・提出、内容世代と writer 結果の対応、completion と退役、停止時の drain |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph.Tests/` | 新設予定の隣接 xUnit project。fake backend による登録・依存・入力世代・所有・失敗の試験 |
| `src/graphics/Lumyte.Graphics.Native.RenderGraph.Tests/Integration/` | DX12／Vulkan での graph 提出と回収の実機試験。CPU 試験とは実行区分を分ける |

現行共通 RenderGraph にある Native 向けの物理計画・記録をこの project へ移す。実際の DX12／Vulkan 呼出しは各 backend、Model／Blur 等の本体と shader は `Lumyte.Graphics.Native.Passes`、DI と shader の非同期取得をつなぐ登録は `Lumyte.Graphics.Native.Hosting` に置く。provider から特定機能の Passes や Hosting へ参照しない。

## 使用例

以下は Hosting integration が非同期準備後に行う下位 composition の例であり、描画利用者の起動コードではない。`BlurPassContract.Instance` は共通の機能契約、`createNativeBackend` は Native backend factory とする。`nativeBlurPackage` は `Lumyte.Resources` 側で展開済みの不変 shader package とし、factory が実装へ渡す。登録中は factory を実行しない。

```csharp
var passes = new NativeRenderPassRegistry();
passes.Register(BlurPassContract.Instance, services => new NativeBlurPass(services, nativeBlurPackage));

providers.Register(new NativeRenderProvider(
    "native-vulkan", createNativeBackend, passes));
```

次は `NativeBlurPass` 作者の `BuildAsync` の要部である。`PrepareBlurAsync` はこの実装の private helper とし、Native shader program、pipeline 記述、内部 view と不変の実行 state を準備する。未変更の pipeline と view 計画を再利用し、今回の radius から実行 state を固定する。その戻り値は必要な shader 世代の lease を含み、`CreateRoot` は Native 専用の root bytes を返す。

```csharp
public async ValueTask BuildAsync(
    NativePassBuildContext context, BlurPassRequest request, BlurPassResult result,
    CancellationToken cancellationToken)
{
    var radius = context.GetInput(request.Radius);
    var state = await PrepareBlurAsync(context, request, result, radius, cancellationToken);
    context.Retain(state);

    context.AddPass("native-blur", state, static (record, value) =>
    {
        var root = value.CreateRoot(
            record.GetShaderIndex(value.SourceView),
            record.GetShaderIndex(value.OutputView));
        record.Commands.SetComputePipeline(value.Pipeline);
        record.Commands.Dispatch(root, value.GroupsX, value.GroupsY);
    })
    .Read(state.Source, new NativePassUsage(
        GpuStage.ComputeShader, GpuAccess.ShaderRead))
    .Write(state.Output, new NativePassUsage(
        GpuStage.ComputeShader, GpuAccess.ShaderWrite));
}
```

この例は全出力 pixel を初期化する一つの内部 pass である。共通の Blur 利用側には shader、index、workgroup 数と内部 AddPass が現れない。provider と pass 実装を配布・登録済みなら、利用側の binary を変更せず provider を選べる。

内容を複数 frame で再利用する場合の登録と取得は次のようになる。`copyPass` は Read／Write を宣言済みの内部 copy、`gpuData` は Native managed ref を含む不変値、`contentLease` はその資源の保持とする。staging は Retain 済みとし、正常に移譲するまでの例外では caller が contentLease を返す。

```csharp
// 内容を作る Build 内。登録だけでは他の Build から使えない。
var next = context.RegisterContent(gpuData, contentLease, [copyPass]);
cachedGeneration?.Dispose();
cachedGeneration = next;
AddDraws(context, gpuData); // 今回の reader も宣言し、copy を依存に残す。

// 後続の Build 内。取得と writer への依存追加を一度に行う。
if (context.TryUseContent(cachedGeneration, out var retainedData))
    AddDraws(context, retainedData);
else
    await RebuildFromSnapshotAsync(context, input, cancellationToken);
```

`AddDraws` と `RebuildFromSnapshotAsync` は作者の private helper とし、それぞれ実際の Read と不足する内容の転送を宣言する。未受理・失敗・回収済みの世代を CPU cache の存在だけで再利用せず、旧 reader の保持は ticket の差替え後も provider に残る。

## 採用範囲と未実装事項

Native pass 本体が shader、GPU data、内部 graph と命令を所有する構成を採用する。NoGraphicsAPI を基礎とする Native の明示 allocation、descriptor と直接 root はその内部で活用し、低レベル wrapper に利用者全体の資源管理を加えない。下位 Native の部分採用事項は担当 ADR に従う。

Native provider、pass registry と構築 SPI、内容世代 ticket の登録・取得・提出結果依存、GetInput と不変 bindings の接続、内部 template と部品の差分準備、共通 facade、実行ごとの内部 graph／transient／記録、alias 切替時の DiscardTexture、pass ごとの shader／cache 管理、resource input と presentation の接続、Hosting integration との登録 snapshot・runtime 単位の factory・所有の接続、同一 consumer binary による適合試験は未実装である。raw external interop の具体 API と共通契約外の Native 専用拡張は、この採用範囲に含めない。
