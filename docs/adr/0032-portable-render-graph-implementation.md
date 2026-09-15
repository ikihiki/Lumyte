# ADR 0032: Portable RenderGraph 実装

## 状態

採用・実装済み。共通の機能 pass を Portable pass 実装で実行する provider、内部計画、内容世代、CPU 準備 cache と明示的な外部 resource 接続を実装する。最初の backend は WebGPU とする。個々の Model／Blur／2D アルゴリズムは機能別 ADR の実装範囲である。

## 依存 ADR

- [0001 Graphics](0001-graphics-api.md): 共通契約と二つの実装系統の境界。
- [0016 Portable Graphics](0016-portable-api.md)〜[0022 Resource Binding](0022-binding-api.md): Buffer／Texture の直接生成、view と明示 binding。
- [0023 Portable Shader](0023-shader-design-and-api.md): WGSL artifact、入力構造体、直接 root と loader。
- [0024 Pipeline](0024-pipeline-state-api.md)〜[0027 WebGPU](0027-webgpu-backend-implementation.md): Portable の記録、提出、完了と backend。
- [0028 Resource Utilities](0028-resource-utilities.md)／[0029 Resource Management](0029-resource-management-api.md): 0028 の Portable resource pool を基礎に、0029 が転送、scope、binding cache、依存と遅延回収を管理する。
- [0030 RenderGraph](0030-render-graph-api.md): 共通の機能 pass 契約、外部入出力、論理 plan、resource facade と実行契約。

## 決定

`Lumyte.Graphics.Portable.RenderGraph` は共通の `IGpuRenderProvider` と `IGpuRenderRuntime` を実装する。共通 graph に追加された Model 描画、Blur、2D 描画などの機能 pass に対し、登録済みの Portable pass 本体を選ぶ。その本体が Portable 専用の shader、内部 graph、binding と command を作る。

利用側の共通契約は機能の入力・出力・効果と追加操作とする。利用者に shader 管理や Portable 向けの Draw／Dispatch 記録を求めない。Native 本体の command または shader 入力だけを変換する方式を採らず、Portable pass の作者がアルゴリズム、draw の分け方、workgroup と必要な shader を独立して決める。

Buffer と Texture はメモリ込みで直接生成する。heap、allocation object、memory requirement、配置 offset と `ExplicitPlacement` は Portable graph に導入しない。resource は有限の binding layout と binding set を使い、Bindless のエミュレーションを行わない。

## API

この package は provider を登録する host と、Portable pass 本体を実装する作者のための API を提供する。機能 pass を利用する application と library の公開 signature にはこの package の型を要求しない。

### Provider と pass の登録

| API | 契約 |
| --- | --- |
| `PortableRenderBackendFactory(options, cancellationToken)` | 共通 runtime 設定から、新しく生成した `IPortableGpuBackend` を `ValueTask` で返す delegate。device の所有を runtime へ渡す。 |
| `PortableRenderProvider(id, createBackend, passes)` | backend factory と `PortableRenderPassRegistry` を使う provider を作る。この時点で device や shader を生成しない。 |
| `Id`／`ContractVersion` | `IGpuRenderProvider` の member。provider 識別子と共通 RenderGraph 契約の version を返す。 |
| `CreateAsync(options, cancellationToken)` | backend、Portable Resources と pass 本体を所有する runtime を生成する。戻り値は共通の `IGpuRenderRuntime` とする。 |
| `PortableRenderPassRegistry.Register<TRequest, TResult>(contract, factory)` | composition 用の bootstrap API。共通 `IGpuRenderPassContract<TRequest, TResult>` に対応する Portable 本体の factory を登録する。契約の `Id`、`Version` と request/result 型が一致する実装を選ぶ。重複する実装は登録しない。 |
| `PortableRenderPassFactory<TRequest, TResult>(services)` | `PortablePassServices` とあらかじめ注入した CPU 依存を借用し、新しい `IPortableRenderPass<TRequest, TResult>` を返す同期 delegate。GPU 生成や I/O は行わない。型付き factory は AOT で直接登録できる。 |
| `IPortableRenderPass<TRequest, TResult>.BuildAsync(context, request, result, cancellationToken)` | 固定 request、今回の不変入力と宣言済み result から、Portable 専用の GPU 配置・転送・shader program と内部 pass を準備する。template と未変更部品を再利用できる。ファイルの取得・解釈は行わない。 |
| `IPortableRenderPass<TRequest, TResult>.DisposeAsync()` | runtime がこの実装の構築と GPU 使用を終了した後、実装が所有する shader cache、scope とその他の保持を終了する。 |

registry は provider 作成時に不変の登録集合として取得する。factory は runtime ごとに pass instance を生成し、runtime 間で WebGPU object を共有しない。同一 instance の `BuildAsync` は runtime が直列化する。記録 callback の state は実行ごとに不変とし、別の構築や caller の変更に依存させない。

通常の起動では Portable の Hosting integration が Generic Host の登録定義と Options を受け、runtime 単位の DI scope から CPU 依存を型付き factory へ渡す。登録集合を固定し、必要な shader package の準備を非同期に完了してから core provider を組み立てる。CPU 依存と package を受け取った closure は runtime の寿命だけ保持し、root singleton に runtime 専用依存や GPU を持つ pass を保存しない。既存 runtime の登録は実行中に書き換えない。

`PortablePassServices` と BuildContext は DI container を公開しない。`IServiceProvider`、Options と Hosting への依存は integration 層に置き、pass の constructor へ型付きの CPU 依存を渡す。BuildAsync や各 frame から service を解決しない。DI の同期 factory は登録・生成方針だけを用意し、GPU program の初期化は専用の非同期準備へ残す。

provider の選択は共通 registry と host 設定で行う。Portable shader の型を利用 library に追加したり、選択 backend に応じて機能 pass の追加コードを分けたりしない。factory は必要な WebGPU 機能と device の生成を担当する。

pass library は必要な shader artifact と入力型を定めるが、パス・URI の解決、stream の読取り、画像・glTF・font の解釈、shader container のデシリアライズは `Lumyte.Resources` の分野とする。この package に asset source や loader の登録 API を追加しない。host から渡された shader package、固定 request と今回の bindings が保持する upload data だけで `BuildAsync` を実行できるようにする。

factory が正常に返した backend と pass は runtime が所有する。factory 内で失敗した部分生成物は factory が回収し、それより前に生成済みのものは runtime 作成処理が回収する。DI から借用した CPU 依存と専用サービスは pass が破棄しない。Hosting integration は runtime の終了後に runtime 単位の DI scope を終了し、同じ pass、manager、backend を DI の別の破棄対象として登録しない。外部 device を借用する場合の所有契約は別の provider 専用 integration に置く。

### Pass 本体のサービスと構築

| API | 契約 |
| --- | --- |
| `PortablePassServices.Backend`／`Resources`／`ShaderLoader` | 同じ runtime の Portable backend、Portable manager と Portable shader loader を借用する。loader は展開済み package から GPU 用 program を作る非 I/O 操作であり、資産 source を持たない。サービス自体を破棄しない。 |
| `PortablePassBuildContext.Services` | この構築に対応する `PortablePassServices` を返す。 |
| `GetInput(value)` | 固定 request の `GpuGraphValue<T>` を、今回の bindings または定数の不変 T に解決する。この pass が ReadInput で宣言した値だけを読める。 |
| `ImportBuffer(resource)`／`ImportTexture(resource)` | 宣言済みの logical resource を今回の `PortablePassBuffer`／`PortablePassTexture` に接続する。resource input は今回の ref、transient は今回の実行に解決する。 |
| `ImportBuffer(reference)`／`ImportTexture(reference)` | 実装が所有・保持する Portable managed reference を内部 graph に取り込む。外部資産の依存は共通契約または package の明示依存で覆う。 |
| `CreateBuffer(name, description)`／`CreateTexture(name, description)` | Portable description を持つ内部 transient を宣言する。後の物理準備で resource を直接生成・再利用する。 |
| `CreateView(name, resource, description)` | Portable の用途・範囲を持つ `PortablePassView` を宣言する。 |
| `CreateBindings(name, program, group, inputs)` | Portable の pass 用に生成した binding 入力と内部 resource/view を使い、`PortablePassBindings` を宣言する。実体の準備は参照先 resource の確定後に行う。 |
| `AddPass<TState>(name, state, record)` | 不変の実行 state と `PortablePassRecordAction<TState>` を登録し、`PortablePassBuilder` を返す。一つの機能から複数回呼べる。 |
| `PortablePassBuilder.Read(resource, usage)`／`Write(resource, usage)`／`ReadWrite(resource, usage)` | 内部 resource の先行内容の読取り／全範囲初期化／部分更新・保持を宣言する。 |
| `PortablePassBuilder.Name`／`Preserve()` | feature 名を含む内部 pass 名と、外部副作用のため culling から保持する明示宣言。共通 feature も Preserve を宣言している場合だけ使える。登録元 Build の終了後は使用宣言を変更できない。 |
| `PortablePassUsage` | `SampledRead`、`UniformRead`、`StorageRead`、`StorageWrite`、`ColorAttachment`、`DepthStencilAttachment`、`CopySource`、`CopyDestination`、`IndexRead`、`IndirectRead` の使用区分。Native barrier や layout state を含めない。 |
| `Retain(lease)` | この実行の記録と GPU 使用が終了するまで必要な `IDisposable` lease の返却責任を provider へ渡す。shader cache の使用 lease などに使う。返却は提出・内容生成の成功通知ではない。 |
| `PortablePassTemplate<TState>(expand)`／`Instantiate(template, state)` | 不変の CPU 展開手順を再利用し、今回の state を結び付ける。過去 execution の logical resource を template に保存しない。 |
| `PortablePassPreparationCache<TKey, TValue>(maxEntries, comparer)`／`GetOrCreateAsync` | pass が持つ不変 CPU 準備結果の LRU cache。既定上限は64件。完全な入力依存を key に含め、失敗・取消し結果は保存しない。GPU 内容の所有は ticket を使う。 |
| `Count`／`Remove(key)`／`Clear()`／`Dispose()` | CPU cache の保持数を読み、指定 entry／全 entry／cache 自体の保持を終了する。値を GPU owner として破棄しない。呼出しは pass の直列化された構築で行う。 |
| `IPortablePassBindingInputs.Write(writer)` | 今回の logical resource を不変の binding 宣言へ書く。生成器がこの interface を実装する。 |
| `PortablePassBindingWriter.Buffer`／`Texture`／`Sampler` | 内部 buffer と範囲、内部 view、managed sampler ref を binding 番号へ対応させる。writer は呼出し終了時に閉じ、sampler は batch が使用保持する。 |

`PortablePassBuffer`、`PortablePassTexture`、`PortablePassView`、`PortablePassBindings` は runtime 内部の非所有参照である。`PortablePassBuildContext` は一回の構築に限って使い、callback に保存しない。`CreateBindings` の入力型は Portable shader metadata から pass 向けに生成し、内部 logical reference を受け取る。resource の実体確定後に下位 Resources の managed binding 入力へ変換し、immutable binding set を準備する。binding 宣言だけでは GPU の使用宣言や shader program の所有を代替しない。

共通 `Declare` は外部の Read／Write／ReadWrite、ReadInput、固定データの ReadUpload と出力を宣言する。Portable 本体はその中を render／compute／copy の内部 pass に展開し、有限の binding、private transient、upload と内部依存を決める。外部の使用資源、更新範囲と結果を契約に一致させる。変更可能な共有 cache や package を隠れた外部入出力として扱わない。

request、GetInput、所有情報を共有する不変 snapshot と UseDeclared は [ADR 0030](0030-render-graph-api.md) の「不変入力と使用保持」に従う。共通 resource description は extent、format、byte 数等の外部意味を示し、usage や shader stage を固定しない。Portable 本体が内部使用を宣言し、provider が実際の用途をまとめて物理 description を具体化する。

内部 pass、resource、view、binding の名前は各カテゴリ内で feature 名を含めて一意とする。共通 `Read` の資源への隠れた書込みと、共通 `Write` の初期化前の読取りを拒否する。共通の書込みは本体の内部書込みで実現する。record context からは、その内部 pass が明示した resource と binding の参照先だけを解決できる。GPU 命令列や native の合法性を解析する検証ではない。

共通 Preserve の宣言だけで不要な内部 writer 全体を保持しない。読取りだけの内部 pass に外部副作用がある場合は、その pass にも `Preserve()` を明示する。初期化せずに読む transient は Preserve でも合法化しない。

### GPU 内容世代の登録と再利用

pass が frame をまたいで保持する GPU 内容は、CPU template と分けて次の SPI で管理する。provider が書込みの提出結果と使用保持を結び付けるための契約であり、低レベル command の状態公開や機能 pass 利用者の登録 API にはしない。

| API | 契約 |
| --- | --- |
| `PortablePassContentGeneration<TContent>` | 同じ runtime の不変な GPU 内容世代を表す opaque ticket。TContent は pass 作者が定める不変の GPU 表現情報と managed ref であり、一回の Build に属する内部 graph 参照を保存しない。内容の直接取得や状態照会は提供しない。 |
| `PortablePassBuildContext.RegisterContent(content, lease, writers)` | 今回の Build が登録した一つ以上の `PortablePassBuilder` を、内容を生成する全 writer として結び付け、ticket を返す。正常復帰時に内容の資源を保持する lease の返却責任を provider へ移す。登録元 Build の使用保持も取得するが、他の Build への利用可能化は writer の受理まで行わない。 |
| `RegisterContent(content, lease, submission)` | 同じ runtime の Portable Resources が発行した `GpuSubmissionToken` で、先行提出してよい private 初期化の受理済み書込みを登録する overload。provider は発行元の completion と結果を追跡する。未管理 raw 提出をこの入口で推測しない。 |
| `TryUseContent(generation, out content)` | ticket が利用可能なら、その世代の保持と書込みへの順序・結果依存を今回の Build に取得して TContent を返す。null、失効・破棄済み、または別の未受理 Build に属する ticket なら false を返し、保持も追加しない。別 runtime の ticket は契約違反として拒否する。 |
| `generation.Dispose()` | cache owner の保持と以後の新規取得を終了する。既に取得した実行や受理済み writer の保持は終了させない。GPU 完了待ち、書込み取消し、成功通知は行わない。 |

`writers` は登録時に固定する。作者は、その内容を作る初期化・更新の全 pass を含め、各 pass に実際の resource の Read／Write／ReadWrite を宣言する。ticket は使用宣言の代わりにならず、未使用 cache を生成するためだけに culling を無効化しない。必要な writer が一つでも culling された場合は ticket を失効させ、未実行の世代を pending のまま cache に残さない。登録元 Build では writer との内部依存を付けて内容を使用でき、後続の Build／記録／提出が失敗した場合は未受理 ticket を失効させる。

全 writer の受理と ticket の利用可能化は provider が一つの操作として確定する。一部の writer だけが受理された場合は、ticket を利用可能にせず、その work の使用保持だけを完了まで維持する。登録元の実行にも writer の結果依存を付ける。未完了の ticket の取得では、WebGPU の同じ queue 上の順序と内部 pass の依存を引き継ぎ、barrier API や CPU の同期 wait を追加しない。内容の成功は [ADR 0026](0026-command-submission-and-synchronization.md) の `WaitAsync` が表す利用終了と診断の確定に従い、`IsComplete` だけでは確定しない。writer の遅延 validation／pipeline エラーでは ticket を失効させ、未提出の利用は提出を中止する。既に受理した利用の completion、export と派生内容にも結果の失敗を伝播し、後続 queue work が終了しただけで成功にしない。

取得・失効・cache owner の返却は provider 内で直列化し、`TryUseContent` の成功直後に失敗が判明しても実行の保持を失わない。provider から pass instance の dictionary を変更する非同期 callback は呼ばない。pass は通常の cache key で ticket を保存し、false なら完全な入力から再構築して古い ticket を Dispose する。元の Build が失敗しても、別の private 初期化が既に受理済みなら、その token に登録した ticket は当該転送自身の結果に従う。

失効した ticket は cache owner の資源保持も終了要求するが、取得済み writer／reader の使用保持を残す。未提出側は Build／記録を停止し、その記録を破棄してから保持を返し、受理済み側は GPU 使用終了または確定した device 停止まで保持する。pass が失効済み ticket を保持し続けても GPU 資源の恒久 owner にはならない。TContent は非所有の実行入力として扱い、次の Build では以前返された値を直接流用せず TryUseContent を通す。内容の資源一覧と書込み範囲は作者の明示契約とし、provider が TContent の reflection や GPU bytes の走査から推測しない。

cache 内容世代の storage は不変とし、旧 ticket の取得可能期間または既存 reader の使用中に同じ bytes を新しい内容で上書きしない。差分更新は別の Buffer／Texture object または非重複 range へ必要な基底と差分を転送して新 ticket にするか、旧保持がすべて終了した pool／ring 領域を使う。作成した新内容の公開で旧 ticket の参照先を差し替えない。単一 execution 内の順序付き ReadWrite は引き続き使えるが、更新される storage を複数の不変 cache 世代として登録しない。物理 heap の配置・alias は追加しない。

### Portable の記録 callback

| API | 契約 |
| --- | --- |
| `PortablePassRecordAction<TState>(context, state)` | 物理 resource と binding、使用保持が準備された後、内部 pass を記録する同期 delegate。 |
| `PortablePassRecordContext.Commands` | provider が所有する Portable の `GpuCommandBuffer` を借用する。作者が render／compute の区間、binding、root と Draw／Dispatch／copy を記録する。 |
| `GetBufferRange(buffer, offset, length)` | 内部 buffer の範囲を `GpuBufferRange` に解決する。 |
| `GetTexture(texture)`／`GetTextureView(view)` | 準備済みの Portable texture handle／`GpuTextureView` を非所有で返す。 |
| `GetBindings(bindings)` | 準備済みの `GpuBindingsHandle` を返す。取得時に binding を生成・更新しない。 |

record context は callback 内だけで使う。command の提出・破棄と queue の所有は provider が引き受け、pass が独立した未管理提出を行わない。record callback は同期処理とし、resource 作成・upload・binding の初回準備は構築と物理準備で完了させる。

root は Portable 用に生成した構造体を直接入力へ渡す。material、Parameter Data、配列は pass 本体の GPU uploader が明示的に作成・upload し、binding は準備済み Buffer の range を参照する。shader が root の index／offset 等から parameter を求め、command が Parameter Data を合成しない。直接 root を uniform／storage buffer に置き換える fallback は設けない。

### 共通 runtime の実装

| 共通 API | Portable 実装の担当 |
| --- | --- |
| `runtime.Resources` | 共通 scope、準備済み package の GPU upload、resource export、pin と回収を Portable Resources に接続する。binding group、WGSL 入力と shader handle は公開しない。 |
| `SubmitAsync(plan, bindings, cancellationToken)` | live な機能に対応する Portable 本体を選び、内部計画と部品を再利用し、今回の入力・資源・binding に必要な準備・記録・下位提出を行う。bindings 省略時は CPU 初期値を使う。queue の受理後に共通 execution を返す。 |
| `WaitIdleAsync(cancellationToken)` | runtime が管理する構築・提出・転送と退役を drain する。外部の未登録提出は対象に含めない。 |
| `DisposeAsync()` | 新しい work の受理を終了し、実行を drain して pass、binding、resource、pool と backend を順に終了する。 |
| `PortableRenderRuntime.Backend`／`PreparationStatistics` | provider integration 用に backend を借用する。統計は CPU schedule cache の保持件数と再利用回数であり、GPU 能力照会や command 状態公開ではない。 |

共通 ref は runtime identity と record 世代を持つ。別 provider/runtime の数値 handle を流用しない。共通 facade の `ImportPackageAsync(data, cancellationToken)` は準備済み `GpuPackageUploadData` を Portable の upload 計画へ接続し、GPU 確保と転送を行う。pass 用 shader のファイル取得・package 展開は `Lumyte.Resources` 側で済ませ、host が不変の Portable shader package を pass factory の closure に渡す。pass 本体は GPU program と専用入力を管理し、`runtime.Programs` のような利用側の shader 管理窓口は設けない。

## 実行計画と最適化

共通 `Compile` は機能契約、入力 slot と外部依存の論理 plan を作る。Portable shader、binding、root bytes と GPU object は生成しない。同じ plan を異なる bindings で繰り返し提出し、内部依存に従って今回の render／compute／copy を具体化する。

内部計画は全範囲 `Write` と先行内容を読む `ReadWrite` を区別する。最終 output、明示 `Preserve`、保持された機能の外部出力、物理資源を伴わない dependency の副作用を根として必要な writer だけを残す。未初期化の読取りは live pass だけで拒否し、上書きされる不要 writer とその資源を準備しない。生き残った pass の登録順で WAR／WAW の順序も保持する。ticket の登録だけでは writer を root にしない。

provider は依存構造から得た live pass の index と resource の生存区間を、上限64件の LRU cache で再利用する。key と値は構造・整数 index のみであり、bindings、record state、GPU ref、以前の BuildContext を保持しない。template は今回の state を新しい execution に束縛し、schedule と生存区間は構造が同じ場合に再計算しない。

Portable 本体と provider は内部 graph の template、CPU 入力の所有情報、geometry／material の GPU 表現、binding 単位の batch、bounds 等を保持し、実際に依存する値や世代が変わった箇所を更新する。BuildAsync は構築と更新の入口であり、毎回すべての内部 node、upload 参照、pipeline 定義を作り直す要件ではない。template が有効なら今回の入力・資源・実行 state を結び付けて再利用する。差分情報が使えない初回、旧世代、cache eviction では完全な snapshot から再構築できる。

camera の変更による culling／透明 sort などは依存に応じて実行する。外部構造が同じまま内部の draw 数や一時資源が変化しても、共通 plan の再 Compile は不要である。WebGPU で毎回 command を記録する場合も、描画データの抽出・packing・binding 準備まで全件やり直す必要はない。command の記録、render bundle 等の再利用と CPU／GPU の分担は Portable 本体が選ぶ。

Portable の Model 描画は material ごとの binding と draw に分けられる。Blur は horizontal／vertical の二つの pass と中間 texture を使ってよい。2D 描画は同じ material、sampler と attachment 条件の work をまとめられる。Native の draw 数、内部 pass 数、workgroup、GPU data の配置に合わせる必要はない。機能の結果と外部依存、精度の契約を維持する。

Native が mesh shader を使う機能にも、この provider は自身の vertex／indexed draw と必要な compute 処理で同じ結果を提供する。Native mesh command、payload、meshlet ABI の変換・エミュレーションを挟まない。shader の計算部分は [ADR 0023](0023-shader-design-and-api.md) の Slang authoring から共有できるが、最終 WGSL、binding と直接 root の配置は Portable の契約に従う。

binding layout は Portable shader package が定義する。pass の生成済み入力から実際に指定した resource/view/sampler/range を解決し、Portable Resources が binding set を作成・再利用する。cache key は layout と各参照先の世代を含める。Native の descriptor index を全 slot の binding 候補に展開する処理は作らない。

必要な物理資源は Buffer／Texture object として作り、description と用途に合う object pool で再利用する。resource の生成前に heap を確保せず、異なる object の物理 memory alias を計画しない。一つの package は明示依存を持つ所有集合であり、単一物理 allocation を意味しない。

同じ execution 内でも完全な description が一致し、生存区間が重ならない transient は同じ object を再利用する。`MarkOutput` と export は実行最後まで保持し、再利用で内容を上書きしない。先の内容が必要な `ReadWrite` を未初期化 transient の先頭に置くことはできない。別 execution 間の pool 再利用は Resources が確認した GPU 利用終了後に限る。

persistent な package export は準備済み `GpuPackageUploadData` の `Profile` と export 契約で利用範囲を定め、対応する Portable uploader が usage を含む完全な物理 description を生成前に package plan へ渡す。`Profile` は用途契約の識別子であり、ファイルや loader の解決先ではない。registry の factory や未実行の BuildAsync から用途を推測しない。再 import と後続機能はその生成契約の範囲で利用し、生成済み object の usage を変更したり、隠れた複製で別用途を満たしたりしない。この生成・所有契約を WebGPU の合法性の独自検証へ広げない。

現行の共通 package importer は `images.sampled` version 1 に対応し、画像の各 subresource と image export を取り込む。Model などが要求する buffer を含む新しい upload profile と、その登録・拡張 SPI は当該機能の実装時に追加する。この対応範囲を汎用の package profile registry の実装済みという意味にはしない。

WebGPU の状態遷移を模倣する barrier stream と Native の stage/access/layout 型を内部計画へ加えない。pass 作者が WebGPU で成立する区間分割と資源使用を選び、wrapper が不適合な同時使用を暗黙の pass 分割で修正しない。

pipeline の論理記述と実体を区別する。必要な raster pipeline の実体化は実際に提出された draw の組合せに対する下位の提出処理で行い、provider 登録や共通 Compile の時点では行わない。`PrepareRasterPipeline` を追加しない。

## 所有、非同期処理と失敗

入力の固定、await 前の使用保持、取消し、提出結果、runtime の終了順序は [ADR 0030](0030-render-graph-api.md) の「所有、同期、失敗」を正とする。Portable 本体は shader と通常の GPU data を自分の cache または scope で所有し、その実行で使う shader lease を Retain、GPU 内容を RegisterContent／TryUseContent に接続する。

runtime は物理 resource、view、sampler と binding を準備してから callback を呼ぶ。batch は binding とその参照先、program/layout、pipeline を completion まで保持する。使用していない global slot の候補をまとめて保持しない。未使用 binding cache が資産を永久保持する root にならないようにする。

準備中に受理された upload は runtime の completion と retirement に登録する。その後の pass 構築が失敗しても、受理済み転送の staging と参照先は完了まで維持する。既存資源の更新は先行する graph 利用と順序付け、caller の CPU memory を非同期操作の契約を超えて参照しない。

準備で先行提出してよい upload は、新しい private 資源・不変世代の初期化など、現在の plan の GPU work に先行できるものに限る。同じ plan の先行 pass が読む資源や共有 cache を更新する場合、BuildAsync は staging と lease を準備し、内部 AddPass の copy command と使用宣言に更新を載せる。staging の使用も内部 graph と Retain に登録し、CPU 準備の順番だけで GPU の先行利用を追い越さない。

共通機能に対応する Portable 本体がなければGPU 資源の準備前に失敗する。必要な shader/package、直接入力または機能条件が揃わない場合は、その準備で失敗する。Native の本体へ切り替えたり、別の意味の処理や Bindless emulation を作ったりしない。実際の WebGPU 生成・提出で初めて判定される失敗は下位の結果を通知する。

GPU 内容の利用可能性は上記 ticket を provider が管理する。Retain の返却や CPU cache の準備完了から推測しない。内容が失敗しても、受理済み work の保持は利用終了が確定するまで維持する。device loss や待機の取消しを利用終了の証拠にせず、GPU 停止を確認する前に resource を pool へ返さない。

外部 resource と command の接続は provider 専用 integration に置き、所有 lease と先行提出の依存を明示する。未申告の外部利用は推測しない。presentation target の再取得条件と command completion は区別し、具体的な WebGPU object や canvas/surface 型を利用側に公開しない。

`PortablePassBuildContext.ImportBuffer(name, handle, description, lease, precedingSubmission)` と Texture overload は、この backend の raw object と完全な description、所有 lease を受け取る。先行する managed 提出がある場合は同じ manager の受理済み token を明示する。null は先行 GPU work がないか caller 側で既に終了したことを表し、未管理 queue work を推測しない。成功時に lease を移譲し、先行提出と今回の batch の両方の利用終了まで保持する。生成済み raw object は pool へ入れず、lease を返すだけとする。

下位 `GpuResourceScope.ImportBuffer`／`ImportTexture` は raw handle と description、lease を managed ref に変換する。`PortableGraphResources.ImportBuffer(reference)`／`ImportTexture(reference)` は同じ manager の ref を共通の非所有 ref へ公開する。元の scope／pin と必要な GPU 保持は integration が明示する。同じ raw handle の二重 import は拒否し、一つの managed ref を共有する。

host／presentation は `PortableGraphResources.ImportBuffer(handle, description, lease, precedingSubmission)` と Texture overload から `PortableGraphResourceImport<TReference>` を取得できる。`Reference` は共通 input に束縛し、`Dispose()` は host の所有だけを終了する。先行 token の結果依存はその ref を使う実行へ引き継ぎ、提出済みの GPU 保持は import owner の返却で解除しない。

`GpuResourceManager.OwnsSubmission` と `RequireAcceptedSubmission` は同じ manager の正常受理 token を確認する公開拡張契約であり、signal 権限を公開しない。`RetainUntilSubmissionEnds(token, lease)` は不確定な提出も含めた同じ manager の token に lease を移し、結果の成功とは独立に GPU 利用終了まで保持する。共通 completion の使用保持もこの契約へ接続する。非同期診断は ticket の失効だけを行い、lease の実際の返却・GPU 破棄は実行 context の `Collect` と終了処理で行う。

resource input による presentation target の差替えは共通 helper と接続する。Acquire 済み target は今回の bindings にのみ結び付け、description が変わる場合は新しい共通 plan を要求する。GPU completion、target の再取得条件と host の frame pacing を分ける。

## 検証

Lumyte が確認するのは機能契約の Id/version と型、runtime identity、record 世代、package と入力型の対応、内部 graph の明示依存、所有と CPU byte 操作の範囲である。WebGPU の usage、format、binding、shader、pipeline と状態遷移の validator は実装しない。compiler と runtime の診断を保つ。

外部契約の意味は Portable 本体の作者が満たす。GPU 命令を解析して Native と同じ処理列であることを確認する設計にせず、同じ共通 request に対する結果、宣言した破壊範囲、依存と失敗契約を適合試験で確認する。

内容世代の試験では、writer 登録後の別 pass の構築失敗、提出失敗、受理済み未完了世代の取得、失効と取得の競合、owner の早期 Dispose と GPU 使用保持を fake completion で確認する。queue の利用終了と遅延診断の確定を両順序で発生させ、writer の失敗が利用側と派生内容へ伝播すること、無関係な世代は失効しないことを確かめる。旧世代を保持した描画の bytes が新世代によって変更されないことも観測する。

### 表示接続 API

`PortableGraphPresentation(resources, surface, size)` は `IPortableGpuSurface` の取得画像を runtime の resource facade に一時 import する。共通 consumer に backend の handle を渡さない。surface が生の texture を所有し、adapter は graph の scope／view を返してから Present／Discard する。Native の queue 操作は runtime の提出と直列化する。実装は `Lumyte.Graphics.Portable.RenderGraph/Presentation/` に置く。

```csharp
await using var presentation = new PortableGraphPresentation(runtime.Resources, surface, GetExtent);
using var context = new GpuRenderContext(runtime, presentation);
using var frame = await context.BeginFrameAsync(cancellationToken);
frame.Graph.AddClearPass("background", new(frame.TargetResource,
    TextureClearValue.Color(new(0, 0, 0, 1))));
using var execution = await frame.SubmitAsync(cancellationToken);
await presentation.WaitForPresentationAsync(cancellationToken);
```

surface は adapter が非同期に破棄する。window／canvas はその完了後に application が破棄する。

## コード配置

以下は repository root 相対の配置である。共通 graph、Portable API、Portable Resources／Shaders への依存に限定し、Native の graph 実装や heap 計画を共有しない。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph/Runtime/`、`Passes/` | PortableRenderProvider、backend factory、PortableRenderPassRegistry と型付き factory。Host 非依存の登録定義 |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph/Passes/` | IPortableRenderPass、PortablePassServices、build／record context、内部 resource と PortablePassContentGeneration の SPI |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph/Planning/`、`Passes/` | 内部 graph、resource usage の集約、schedule／生存区間 cache と logical binding の具体化 |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph/Resources/` | 共通 facade の Portable 実装、ref の対応、upload profile、Buffer／Texture の生成と transient／使用保持 |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph/Runtime/`、`Passes/` | 共通 runtime、各 pass の記録と下位提出、内容世代と writer 結果の対応、completion、失敗・終了時の回収 |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph.Generators/` | build 用 project。準備済み `.portable.resources.xml` から CreateBindings 向けの Buffer／View／Sampler／range 入力型を生成する。Resources 向け managed 入力生成とは metadata で選択し併用できる |
| 利用 project の `obj/<Configuration>/<TargetFramework>/Shaders/Portable/Graph/` | 上記生成器の出力。feature 本体の build へ取り込み、手書き source と別にする |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph.Tests/`、`src/graphics/Lumyte.Graphics.Portable.RenderGraph.Generators.Tests/` | 隣接 xUnit project。fake backend の計画・binding・所有試験と、生成入力を compile／使用する consumer 試験 |
| `src/graphics/Lumyte.Graphics.Portable.RenderGraph.Tests/Integration/` | WebGPU での実行、binding と回収の実機試験。通常の CPU 試験から分ける |

生成器は feature 実装の build から使用し、shader package／loader と Resources の runtime に上位 Graph への依存を追加しない。WebGPU の native／browser 接続は各 backend、機能本体と Portable 用 Slang／WGSL source は `Lumyte.Graphics.Portable.Passes`、共有計算 module は `src/graphics/Shaders/Shared/`、DI 登録は `Lumyte.Graphics.Portable.Hosting` に置く。生成 WGSL は obj 配下に分離する。共通 RenderGraph に旧 binding／記録実装を残して Portable をそこへ接続する互換経路は設けない。

## 使用例

以下は Hosting integration が非同期準備後に行う下位 composition の例であり、描画利用者の起動コードではない。`BlurPassContract.Instance` は共通の機能契約、`createPortableBackend` は WebGPU backend factory とする。`portableBlurPackage` は `Lumyte.Resources` 側で展開済みの不変 shader package とし、factory が実装へ渡す。登録中は factory を実行しない。

```csharp
var passes = new PortableRenderPassRegistry();
passes.Register(BlurPassContract.Instance, services => new PortableBlurPass(services, portableBlurPackage));

providers.Register(new PortableRenderProvider(
    "portable-webgpu", createPortableBackend, passes));
```

次は `PortableBlurPass` 作者の `BuildAsync` の要部である。`PrepareAxesAsync` はこの実装の private helper とし、Portable shader program、中間 texture、二つの軸の binding 宣言、pipeline 記述と不変の実行 state を準備する。未変更の pipeline と binding 計画を再利用し、今回の radius から実行 state を固定する。`axes` は必要な shader 世代の lease を含み、各軸の root は Portable 専用型である。

```csharp
public async ValueTask BuildAsync(
    PortablePassBuildContext context, BlurPassRequest request, BlurPassResult result,
    CancellationToken cancellationToken)
{
    var radius = context.GetInput(request.Radius);
    var axes = await PrepareAxesAsync(context, request, result, radius, cancellationToken);
    context.Retain(axes);

    foreach (var axis in axes.Passes)
    {
        context.AddPass(axis.Name, axis, static (record, value) =>
        {
            var commands = record.Commands;
            commands.BeginCompute();
            commands.SetComputePipeline(value.Pipeline);
            commands.SetComputeBindings(0, record.GetBindings(value.Bindings));
            var root = value.Root;
            commands.SetComputeRootData(in root);
            commands.Dispatch(value.GroupsX, value.GroupsY);
            commands.EndCompute();
        })
        .Read(axis.Source, PortablePassUsage.SampledRead)
        .Write(axis.Output, PortablePassUsage.StorageWrite);
    }
}
```

第一の pass は source から中間 texture、第二の pass は中間 texture から共通の出力を生成する。各出力は全 pixel を初期化し、二つの pass は同じ中間 resource の Write／Read で順序付ける。利用側は同じ Blur 追加 API を使い、shader、binding と pass 分割を管理しない。

内容を複数 frame で再利用する場合の登録と取得は次のようになる。`copyPass` は Read／Write を宣言済みの内部 copy、`gpuData` は Portable managed ref を含む不変値、`contentLease` はその資源の保持とする。staging は Retain 済みとし、正常に移譲するまでの例外では caller が contentLease を返す。

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

`AddDraws` と `RebuildFromSnapshotAsync` は作者の private helper とし、それぞれ実際の Read と不足する内容の転送を宣言する。取得時点で writer の診断が未確定なら provider が結果依存を持ち、後から失敗した内容を成功した描画として返さない。bind group の存在や Retain の返却から内容の成功を判定しない。

## 採用範囲と未実装事項

Portable pass 本体が shader、GPU data、内部 graph と命令を所有する構成を採用する。resource の直接生成と有限の明示 binding を用い、NoGraphicsAPI の allocation、GPU address や Bindless の模倣を要件にしない。

Portable provider、pass registry と公開構築 SPI、GetInput と不変 bindings、共通 facade、内部依存と culling、初期化・外部契約検査、template と bounded CPU 準備 cache、transient object 再利用、内容世代 ticket、先行 upload と遅延診断の結果依存、明示 raw import、管理された view／binding、scope／batch／export pin、診断付き completion と成功後の export を実装した。標準画像処理七機能と 2D は別の Portable.Passes assembly から登録し、Output の専用 WGSL と直接 root を本体が所有する。

Graph 用 binding 入力生成器と consumer 試験も実装し、標準 Output が生成された入力型を利用する。Clear は全 mip／layer／depth slice の初期化を一つの内部 pass の全範囲 Write として宣言し、culling で一部の clear が失われない。

標準 Output で直接 WGSL を使うことを Slang source の共有完了とは扱わない。Blur／2D 本体と window／canvas の adapter は各担当 assembly に実装済みである。Model の完成は専用 ADR で管理する。共通 facade の明示 resource 操作は caller が直列化し、外部所有の scope／pin／execution は caller が返す。cache ticket の owner を返し忘れた場合も runtime の終了が所有を終了する。Portable に物理 heap alias、Bindless emulation、未管理外部提出の自動検出を追加しない。
