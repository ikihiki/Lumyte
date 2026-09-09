# ADR 0030: 機能 pass を組み立てる共通 RenderGraph API

## 状態

採用（目標設計）。利用 library／application は一つの共通 assembly に対してコンパイルし、Native／Portable を実行時に選択する。共通化するのは機能 pass の要求と入出力であり、GPU command を記録する pass 本体は二系統で実装する。現行実装の完了を示さない。

## 依存 ADR

| ADR | 利用する契約 |
| --- | --- |
| [0001 Graphics](0001-graphics-api.md) | 共通の公開契約と二系統の実装の境界 |
| [0015 Native Shader Package](0015-native-shader-package-api.md)・[0023 Portable Shader](0023-shader-design-and-api.md) | 準備済み shader package からの GPU program 生成と専用 GPU 構造体 |
| [0012 Native 提出と同期](0012-native-command-submission-and-synchronization.md)・[0026 Portable 提出と同期](0026-command-submission-and-synchronization.md) | 実行系統ごとの提出と completion |
| [0028 Resource Utilities](0028-resource-utilities.md)・[0029 Resource Management](0029-resource-management-api.md) | 使用保持、資源管理と回収 |

これは設計上の依存であり、共通 assembly から Native／Portable の型や assembly を参照することを意味しない。各 provider が共通契約と自分の実装系統に依存する。

## 決定

公開 assembly／namespace は一つの `Lumyte.Graphics.RenderGraph` とする。利用側は「Model を描く」「ブラーをかける」「2D を描く」といった pass を、共通の CPU 入力と logical resource を使って追加する。機能 library の extension は共通の `AddPass(name, contract, request)` に要求を登録する。

**共通 AddPass は GPU 記録 callback を受け取らない。** 共通の Draw／Dispatch／copy 命令列や、shader の共通入力 ABI も定義しない。Native／Portable の pass 実装がそれぞれ内部 graph を組み立て、自分の低レベル command API で記録する。shader、生成構造体、loader、pipeline、descriptor／binding はその実装が管理する。

資産のロード、URI 解決、ファイル形式の解釈は `Lumyte.Resources` の分野とする。実行時の階層・animation・物理の評価や手続き生成は ECS／scene 等の上位も行い、準備済みの不変データを直接供給できる。Graphics は受け渡し型だけを定め、ID からデータを読み出す interface や Entity／Component の管理 API は定義しない。

一つの機能 pass を、片方では一回の compute、もう片方では二回の raster pass に展開してよい。外部の入力・出力・副作用の契約を守る範囲で、内部 pass 数、algorithm、GPU data layout、workgroup、batching を独立して決める。

同じ Model 要求を Native が mesh shader、Portable が vertex／indexed draw で実装してよい。共通 request に mesh stage、workgroup 数、meshlet または shader 選択を追加しない。Slang の計算 module を共有する場合も、内部 graph と専用 GPU ABI は別々に構築する。

pass と外部依存の構造を、フレームごとに変わる入力から分離する。構造を一度 Compile し、次のフレームでは型付き入力の bindings だけを更新して同じ plan を提出する。Model の描画項目の増減、camera、変換、材質、動的 geometry、色や半径の変更だけで共通 graph を再構築する必要はない。外部の使用資源・依存・出力形状を変更する場合は新しい graph を Compile する。

## API

### 機能 pass の要求と宣言

| API | 契約 |
| --- | --- |
| `IGpuRenderPassContract<TRequest, TResult>` | 機能 pass の共通 CPU 契約。実装系統に依存しない。 |
| `contract.Id / Version` | 機能と契約版を識別する。shader ID／shader ABI の版ではない。 |
| `contract.Snapshot(request)` | 外部 resource と入力 slot を含む要求構造を不変にする。登録後に caller が変更しても構造は変わらない。フレーム入力の内容は各 input contract が固定する。 |
| `contract.Declare(context, request)` | 不変 request から固定の外部 resource 使用、入力 slot／定数と出力を宣言し、共通 `TResult` を返す。フレーム値の読取り、GPU 処理、shader ロード、backend 分岐を行わない。 |
| `graph.AddPass(name, contract, request)` | Snapshot と Declare を呼び、機能要求を登録して `TResult` を返す。GPU 記録 delegate は引数に持たない。 |
| `GpuPassDeclarationContext.CreateBuffer/Texture(name, description)` | 機能 pass が外部へ公開する logical output を宣言する。内部の一時資源はここに含めない。 |
| `Read(resource)`／`Write(resource)`／`ReadWrite(resource)` | 先行内容の読取り／全体初期化／先行内容の保持または部分更新を宣言する。GPU stage、access mask、layout は指定しない。 |
| `Read(dependency)`／`Write(dependency)` | memory access を伴わない順序依存を宣言する。 |
| `ReadInput(value, inputContract)` | `GpuGraphValue<T>` の slot または定数と、その型の snapshot／所有契約を pass に登録する。slot 作成時と同じ input contract を使う。外部 resource の依存は追加しない。 |
| `ReadUpload(data)` | 要求構造に固定した準備済み転送データの所有を保持する。変更する値は ReadInput に分離する。 |
| `Preserve()` | 明示した外部副作用のため、この機能 pass を culling で除去しない。 |

`TRequest` と `TResult` は機能 library が一度だけ定義する。行列、色、半径、モデルの転送データ、2D scene、logical texture などを扱う。GPU pointer、descriptor index、binding group、shader program、GPU 向け構造体は含めない。request の CPU 配置を shader ABI とみなさない。

Declare は外部に見える振舞いの契約である。例えば Blur は入力画像を Read し、出力画像全体を Write する。2D 合成で既存の色を残す場合は対象を ReadWrite する。どの stage や view を使うかは各実装に委ねる。変わる CPU データの所有は input contract に、実際の GPU 使用登録は pass 本体に分ける。

Snapshot 後に caller の配列を変更しても登録済み要求は変わらない。転送データは不変の内容世代を持つ所有済みの値として保持する。Declare が返す TResult は graph の出力参照などの不変値とし、実行時の GPU 結果を CPU へ返す仕組みにはしない。

### 固定構造とフレーム入力

| API | 契約 |
| --- | --- |
| `GpuGraphValue<T>.Constant(value)`／`FromInput(input)` | CPU 定数または `GpuGraphInput<T>` の参照を表す。不変の和型とし、T と input からの暗黙変換も用意する。 |
| `IGpuGraphInputContract<T>.Snapshot(value)` | 値を所有済みの不変 snapshot にする。不変値や共有部分木はそのまま使い、可変の借用 memory は固定する。 |
| `IGpuGraphInputContract<T>.Retain(context, snapshot)` | snapshot が直接参照する upload data と logical resource を型に従って登録する。GPU 処理、外部依存の追加、ロードは行わない。 |
| `GpuRenderInputRetentionContext.ReadUpload(data)` | 準備済みデータと、その明示的な子参照の CPU 所有を保持する。GPU allocation や cache entry の使用保持とは区別する。 |
| `GpuRenderInputRetentionContext.UseDeclared(resource)` | 入力内の logical resource が、その pass に固定宣言された Read／ReadWrite 集合にあることだけを確認する。新しい edge は追加しない。 |
| `graph.CreateInput(name, inputContract)`／`CreateInput(name, inputContract, initialValue)` | graph と型を識別する `GpuGraphInput<T>` を返す。初期値を渡した場合だけ Snapshot して既定値として保持する。省略した slot は提出までに Set が必要。 |
| `plan.CreateBindings()` | この plan の指定済み CPU 初期値を持つ `GpuRenderGraphBindingsBuilder` を作る。初期値のない CPU input と resource input は未設定とする。 |
| `bindingsBuilder.Set(input, value)` | CPU input の Snapshot をこの呼出し時点で確定する。builder を再利用すると、未変更 slot は直前の builder 値を保つ。 |
| `bindingsBuilder.Build()` | plan identity と入力世代を持つ不変の `GpuRenderGraphBindings` を返す。以後の builder 更新は反映しない。 |

入力の意味と所有は機能契約版の一部とし、input contract に別の loader、検証 service や履歴照会 API は加えない。定数も ReadInput に渡した input contract で一度 Snapshot する。値だけの input contract は Retain で何も保持しなくてよい。同じ slot を複数 pass で使う場合も同じ snapshot を共有し、UseDeclared は使用する pass ごとの固定宣言に対して確認する。

Retain はデータ型が明示した参照から、所有に必要な集合を不変の snapshot／部分木に結び付ける。未変更部分木の子データを提出ごとに再列挙せず、保持済みの所有情報を共有する。新しい枝だけを追加し、同じ入力を別 plan／pass で使うときはその固定宣言との対応を確認する。reflection、GPU pointer の走査、全内容の hash、外部 ID の解決で保持先を推測しない。保持情報を含めた snapshot は常に完全な値であり、差分履歴が失われても実行できる。

入力に含まれる logical texture／buffer は固定宣言の範囲から選ぶ。例えば 2D の scene が複数画像を切り替えるなら、使い得る logical texture を構築時に Read し、各 snapshot が UseDeclared で対応を示す。宣言外の画像を後から追加する場合は graph を作り直す。準備済み画像データなど pass 本体の内部資源が増減することは、外部 logical resource の追加とは扱わない。

bindings は「GPU 上の最新版」を指す窓口ではない。初期値と Set で固定した値だけを使い、前回の実行結果や ECS の更新を読み直さない。大きい動的 scene 等は初期値なしで slot を作り、今回の値を bindings だけに持たせれば、plan が最初の scene を永久に既定値として保持することを避けられる。古い bindings、更新世代を飛ばした bindings、同じ入力を異なる camera で使う複数 pass も提出できる。差分の準備は実際に保持する基底との比較で行い、直前の Submit が必ず一つ前の世代だと仮定しない。

### Graph と logical resource

| API | 契約 |
| --- | --- |
| `GpuRenderGraph` | 機能要求と外部 resource の依存を保持する。GPU object を所有しない。 |
| `GpuGraphBufferDescription(Size)` | 外部契約で byte format が共通な Buffer の論理 byte 数。heap、alignment、GPU usage を指定しない。 |
| `GpuGraphTextureDescription` | dimension、extent、mip/layer count、sample count、format。GPU usage、view 目的、binding は各実装が決める。 |
| `GpuGraphTextureDimension` | `OneD/TwoD/ThreeD` の形状。format には共通の `GpuFormat` を使う。 |
| `GpuRenderGraphBuffer`／`GpuRenderGraphTexture` | graph と宣言を識別する logical resource。`Description` は宣言時の値を返し、native API の照会は行わない。 |
| `GpuRenderGraphDependency` | memory を持たない順序依存。 |
| `CreateBuffer/Texture(name, description)`／`CreateDependency(name)` | logical resource／順序依存を宣言する。物理確保は provider が行う。 |
| `ImportBuffer/Texture(name, reference)` | 同じ runtime に属する共通 managed ref を固定の借用として宣言する。 |
| `CreateBufferInput(name, description)`／`CreateTextureInput(name, description)` | 毎回差し替える資源の `GpuGraphBufferInput`／`GpuGraphTextureInput` を作る。`Buffer`／`Texture` が固定の logical resource を返す。 |
| `bindingsBuilder.Set(resourceInput, reference)` | 同じ runtime の managed ref を資源 slot に設定する。GPU 使用保持は提出時に取得する。 |
| `MarkOutput(resource)` | culling の到達点にする。 |
| `ExportBuffer/Texture(resource)` | transient を output にし、実行後も参照できるよう宣言する。 |
| `Compile(cache = null)` | 不変の `GpuRenderGraphPlan` を作る。機能要求と外部依存を culling・順序付けする。 |

Compile は CPU の graph 処理であり、application assembly の再コンパイルではない。shader ロード、二系統の pass 本体の呼出し、GPU 記録はこの段階では行わない。pass 名と内部名は機能 instance ごとの名前空間で分離する。slot の値を変更しても同じ plan を使う。pass の追加・削除、外部依存、資源 description または定数を含む要求構造の変更では、新しい graph を Compile する。

resource input の description と宣言した用途は plan 内で固定する。今回の managed ref がその runtime／世代と description に対応することを提出前に確認する。異なる slot 同士、または slot と固定 import が同じ物理 resource を指すことは許可しない。必要な共有は同じ logical resource を使って宣言する。これは見えない依存を作らないための共通 graph の契約であり、native API の memory alias の合法性を調べる validator ではない。

CPU／resource input が未設定でも Build は可能だが、live な使用に必要な全 slot を解決してから Submit する。未設定、失効した ref、意図しない alias は準備前に失敗する。target を取得する helper は提出前に今回の target 値だけを補う。

外部 resource の使用は AddPass の登録順で論理的な内容の版へ対応付ける。Read はその時点の先行内容、Write は新しい内容、ReadWrite は先行内容を読んだ上での新しい内容を表す。上書き前の reader が読むまで次の writer を進めない順序も残す。MarkOutput／Export は呼出し時の内容を到達点とする。同じ texture を更新する 2D 合成を自己依存とせず、依存のない機能だけを並べ替える。

一般の shader 構造体配列は各実装の内部資源とする。共通 byte Buffer の型だけで、target 固有の構造体 bytes や pointer を移植可能とは扱わない。共通入力・出力に Buffer を公開する機能は、その byte format 自体を契約に含める。

### GPU への受け渡しデータ

| 型 | 契約 |
| --- | --- |
| `GpuUploadDataKey(Id, Revision)` | 不変の内容と世代を識別する。cache の同一性に使い、URI、ファイル名、ロード用 key として解決しない |
| `IGpuUploadData.Key` | 全 transfer input の共通部分。データはそれぞれの具体型に保持し、Load／Resolve 等の操作は持たない |
| `GpuBufferUploadData(Key, Data)` | 共通の byte format が定義された、不変の全 byte 列。GPU address／descriptor index を埋め込まない |
| `GpuImageUploadData(Key, Description, Encoding, AlphaMode, Subresources)` | decode 済みの画像。Description は共通 texture の形状と format、subresource は画素／block data を保持する。sample count は 1 とする |
| `GpuImageSubresourceData(MipLevel, ArrayLayer, RowStride, SliceStride, Data)` | 指定 mip／layer の CPU 転送元。stride は byte 単位。3D の各 depth slice は SliceStride で表す。native API の配置 footprint ではない |
| `GpuImageColorEncoding` | `Linear`／`Srgb`／`Data`。色の復号が必要か、法線等の数値データかを表す |
| `GpuImageAlphaMode` | `Opaque`／`Straight`／`Premultiplied`。転送後の機能で二重に alpha を乗算しないための意味 |
| `GpuPackageUploadData(Key, Buffers, Images, Exports, Profile)` | 準備済み Buffer／Image データと export の不変集合。ファイル上の package 形式、archive、lazy reader を含めない |
| `GpuPackageUploadExport.Buffer(Name, Index)`／`Image(Name, Index)` | 共通 export 名と package 内のデータを対応付ける。shader 用 GPU 構造体を export しない |
| `GpuUploadProfileId(Name, Version)` | package export の用途契約を識別する。対応する GPU 生成方針は provider が実装し、ロード先を指定しない |

`GpuBufferUploadData`、`GpuImageUploadData`、`GpuPackageUploadData` は IGpuUploadData を実装する。subresource／export の記述値は親の所有に従う。Model や glyph のような機能固有データは、それぞれの ADR でこの共通境界に接続する型を定める。GPU への物理 packing、row pitch の調整、shader ABI は Native／Portable の本体が決める。

転送データは、自分で所有する不変 memory を持つ。Lumyte.Resources、ECS／scene、手続き生成等の供給側から所有を移譲するか、必要な内容をコピーした snapshot を受け渡す。ReadOnlyMemory で包んだだけの借用配列、閉じた stream、解放される Resource lease に依存する memory は渡さない。元の資産 cache の eviction や次フレームの Component 更新があっても、保持中の transfer input は有効でなければならない。

同じ Key は同じ内容を表す。内容の変更時には新しい Revision を発行し、実行中の入力を更新しない。静的資産と毎フレーム変わるデータは同じ転送型と世代契約を使い、機能側が定めた独立データのうち変更したものだけを新世代にできる。これは供給側のデータ契約であり、Graphics が外部資産の最新版を問い合わせたり、全データの hash を再計算したりする要件ではない。

初期の共通 package profile は `images.sampled` version 1 とし、export は画像、利用は shader 読取りに限定する。入力は sRGB primaries の線形 premultiplied RGBA に準備済みとし、Encoding は Linear、AlphaMode は Premultiplied または alpha が常に 1 の Opaque、format は sRGB 自動復号を行わない格納形式を使う。export はその形状・format・画素表現を維持し、Blur や 2D の logical texture 入力として使える。provider は転送先と読取りに必要な用途で生成し、この import に色変換や premultiply を暗黙に追加しない。別用途の共有 package は対応 profile を両 provider に実装してから使う。Model／2D 等の内部資源はこの汎用 profile に押し込まず、各機能が受け取る型と内部使用から生成する。

### Runtime の選択と提出

| API | 契約 |
| --- | --- |
| `GpuRenderProviderRegistry.Register(provider)` | composition 用の bootstrap API。利用可能な `IGpuRenderProvider` を ID と共通契約版で登録する。device を所有しない。 |
| `registry.CreateAsync(options, cancellationToken)` | integration または手動 composition が、登録と設定の不変 snapshot から provider を選び、`IGpuRenderRuntime` を生成する入口。通常の描画利用者は呼ばない。 |
| `GpuRenderRuntimeOptions` | `ProviderId`（設定の ID または Auto）、`RequiredPasses`（機能 ID／版）、`EnableValidation`。shader ID や code format は要求しない。 |
| `IGpuRenderProvider.Id / ContractVersion` | host の登録と選択に用いる識別子と共通契約版。 |
| `IGpuRenderProvider.CreateAsync(options, cancellationToken)` | provider が runtime を作る実装入口。描画 library は直接呼ばない。 |
| `IGpuRenderRuntime.Resources` | 共通の `IGpuGraphResources`。選択系統の manager に接続する。 |
| `runtime.SubmitAsync(plan, bindings, cancellationToken)` | plan と不変 bindings の使用保持を取得し、専用実装の再利用・差分準備・記録・提出を行って `GpuRenderGraphExecution` を返す。bindings 省略時は CPU 初期値を使う。 |
| `WaitIdleAsync(cancellationToken)`／`DisposeAsync()` | 自分の work と管理資源を終了する。未知の外部 GPU 利用は対象にしない。 |

RequiredPasses は host の初期選択に使う機能 manifest である。個々の計画も、必要な契約版の実装が選択 provider に登録されていることを準備前に確認する。対応する二系統の pass 実装と shader artifact は実行環境へ配布しておく。artifact の I/O と展開は Lumyte.Resources が担当し、host が準備済みの各系統の package を pass factory へ渡す。GPU program の生成と使用保持は pass 実装が担当する。利用側に shader のロード、program の保持、binding の作成を要求しない。

Generic Host を使う通常経路では、別の Hosting integration が service collection、Options と型付き factory の登録定義を起動時に一度集める。runtime 作成の前に provider／pass の登録集合と設定を固定し、以後の登録変更を既存 runtime に反映しない。登録処理と DI の同期 factory では GPU 生成や I/O を行わない。shader の I/O は非同期初期化から Lumyte.Resources に委譲し、consumer は DI された利用準備完了の入口を一度 await して runtime を借用する。毎フレームの登録、DI 解決や CreateAsync は不要である。

この core API は `IServiceCollection`、`IServiceProvider`、Options と Generic Host に依存しない。registry は service locator ではなく、明示的な provider と専用 pass factory の不変対応を保持する。DI が所有するのは Hosting owner と CPU 依存であり、選択 runtime が所有する GPU object を DI へ別 owner として登録しない。手動 composition でも同じ core API と所有契約を利用できる。

提出の流れは次のとおりとする。

1. plan と bindings の対応を確認し、live な機能を選択 provider の実装へ対応付ける。外部 slot を解決し、最初の非同期中断より前に今回の CPU snapshot と import／資源 slot の使用保持を取得する。
2. 不変の入力所有情報、内部 graph の template と GPU 部品を再利用し、必要な箇所を BuildAsync で準備する。GPU program の初期化、専用配置への変換、明示 upload と cache は各実装が担当し、ファイル取得・decode は開始しない。
3. 今回の入力を内部 graph へ結び付け、実際の資源使用と外部の順序依存を接続する。構造が変わらない内部計画は再利用できる。
4. provider が実行ごとの配置、view、descriptor／binding、同期と使用保持を確定し、自系統の callback で必要な command を記録する。
5. 下位の Submit に渡す。実際に提出する使用組合せの PSO はそこで解決する。queue が受理したら共通 execution を返す。

SubmitAsync の成功は GPU 完了を意味しない。非同期にする理由は GPU 資源の準備、転送と使用の順序付けを行えるようにするためである。準備済みの shader や pipeline 定義は各実装で再利用できるが、利用側へ別の shader 準備 API を設けない。

### 永続資源と所有

| API | 契約 |
| --- | --- |
| `GpuGraphBufferRef`／`GpuGraphTextureRef` | runtime と record 世代を識別する非所有の共通参照。下位 handle、address を取得する member は持たない。 |
| `IGpuGraphResources.CreateScope()` | 共通の `GpuGraphResourceScope` を作る。 |
| `scope.ImportPackageAsync(data, cancellationToken)` | 準備済みの `GpuPackageUploadData` から GPU 資源を生成・転送し、当該転送の診断を含む成功確認後に `GpuGraphPackageRef` を返す。scope が所有する。ファイルの読取りは行わない。 |
| `GpuGraphPackageRef.GetBuffer/Texture(exportId)` | 宣言済みの共通 export ID から非所有 ref を返す。shader や target 固有の data layout は export しない。 |
| `scope.Release(reference)`／`Dispose()` | scope の保持を放す。取得済み pin／GPU 使用保持は終了しない。 |
| `IGpuGraphResources.Pin(reference)` | 明示依存を含めた `GpuGraphResourcePin` を返す。`Dispose()` でその保持を放す。 |
| `Collect()`／`Trim()` | 完了済み退役を回収する／完全に未使用の cache と資源を解放する。GPU を待たない。 |

通常の Model や 2D は機能 request に準備済みの転送データを渡し、GPU への配置と転送を各 pass 実装が行う。共通 package の import は画像などを複数機能で明示的に共有する場合の GPU upload 入口であり、ロード機能を兼ねない。利用者に各 shader の resource や GPU ABI を準備させない。

共通 package の Profile は export の byte／pixel 表現と機能上の利用範囲を定める。対応する provider の upload 実装が、その契約から usage を含む物理 description を生成前に決める。これは既知のデータ契約の GPU への具体化であり、profile 名からファイルや decoder を探さない。未知の profile／版は自分の契約の不足として受け付けない。graph の transient を export した場合も、その実行で生成した用途を維持する。

物理 resource の生成、mapping、upload、sampler、descriptor／binding の操作は専用 Resources と pass 実装に置く。共通型へ包み直した同じ低レベル操作を一式公開しない。Native の統一 allocation と Portable の Buffer／Texture 直接生成の違いも実装内に閉じる。

### Plan、execution、completion

| API | 契約 |
| --- | --- |
| `GpuRenderGraphPlan.Passes / Resources` | 外部の機能順序、宣言、論理 first/last use と export の診断値。内部 GPU pass 数は表さない。 |
| `GpuRenderGraphPlanCache(maximumEntries)` | graph を再 Compile するときの有界の論理構造 cache。`Count`／`Clear()` を公開し、過去フレームの bindings や GPU object は保持しない。 |
| `plan.SubmitAsync(runtime, bindings, cancellationToken)` | runtime の提出入口に委譲する。bindings の省略は初期値の提出を表し、同じ plan を繰り返し使える。 |
| `GpuRenderGraphExecution.Completion`／`IsComplete` | 共通 `GpuGraphCompletion` と GPU 使用終了の状態。IsComplete は回収の条件であり、処理の成功を証明しない。native semaphore/value は公開しない。 |
| `GpuGraphCompletion.WaitAsync(cancellationToken)`／`execution.WaitForCompletionAsync(cancellationToken)` | GPU 使用終了と、この実行および内容依存の診断確定を待つ。正常終了で成功を確認し、提出後の失敗は原因を保持して通知する。 |
| `execution.GetExportedBuffer/Texture(resource)` | 成功確認済み execution が保持する共通 managed ref を返す。結果未確定では取得せず、WaitForCompletionAsync の正常終了後に呼ぶ。失敗した出力は返さない。 |
| `execution.Dispose()` | execution の所有を終了する。GPU 使用と他の保持が終わってから実回収する。 |

### Frame と presentation

| API | 契約 |
| --- | --- |
| `GpuRenderGraphFrameBuilder.AddContributor(name, state, callback, order, enabled)` | 機能 pass を追加する CPU callback を登録する。GPU 記録 callback ではない。 |
| `BuildGraph()`／`Compile()` | order、次に ordinal name の順で有効な contributor を実行する。 |
| `GpuRenderGraphContributionContext.Graph` | 名前を分離して共通 graph を構成する窓口。 |
| `PublishTexture/Buffer/Dependency`／`GetTexture/Buffer/Dependency` | contributor 間の logical resource の受渡し。 |
| `IGpuGraphPresentation.AcquireNextTargetAsync(cancellationToken)` | 共通 texture ref、description と所有 token を持つ `GpuGraphPresentationTarget` を取得する。 |
| `Present(target, completion)`／`Discard(target)` | 提出済み target を presentation に渡す／未提出 target を返す。 |
| `GpuRenderContext(runtime, presentation, cacheMaximumEntries)` | runtime と presentation を借用する共通 frame helper。 |
| `GpuRenderContext.SubmitAsync(plan, bindings, presentationTargetInput, cancellationToken)` | target を取得し、今回の target を不変 bindings の派生値へ設定して runtime に提出し、Present して execution を返す。 |
| `GpuPresentationTargetChangedException.Description` | 取得した target と plan の形状が一致しない場合に、その target の共通 description を通知する。helper が未提出 target を返却した後に送出する。 |
| `BeginFrameAsync(cancellationToken)` | 単発の graph 構築用に target を import した `GpuFrame` を返す。 |
| `GpuFrame.Graph / TargetResource` | frame の共通 graph と output resource。 |
| `GpuFrame.Retain(lease)` | 明示した外部 lease を frame と提出済み execution に保持する。 |
| `GpuFrame.SubmitAsync(cancellationToken)` | target を output にして Compile、SubmitAsync、Present を行い execution を返す。 |
| `GpuFrame.Dispose()`／`GpuRenderContext.Dispose()` | 自分の未提出 target／保持／cache を終了する。借用 runtime は破棄しない。 |

継続描画では target の description から texture input を宣言して plan を作り、GpuRenderContext.SubmitAsync を繰り返す。helper は Acquire → 今回の target を設定 → Submit → Present を行い、caller の bindings を変更しない。presentationTargetInput は plan に属し、書込みと MarkOutput が宣言済みでなければならない。description が変わる resize 等では新しい plan を作る。GpuFrame と contributor の BuildGraph は単発描画や構造変更時の入口とし、毎フレーム呼ぶことを要求しない。

target と input の description の照合は取得後、pass の BuildAsync より前に行う。不一致では当該実行の準備・提出・Present を開始せず、保持を返して GpuPresentationTargetChangedException を通知する。呼出し元は通知された description で plan を作り直し、次の取得から再試行できる。再試行までに再度 resize された場合も同じ扱いとする。他の提出失敗を resize とみなして自動再提出しない。これは取得済み target と自分の plan の照合であり、native resource の照会 API ではない。

helper は Acquire の最初の await より前に、今回の CPU snapshot と target 以外の import／資源 slot の使用保持を確保する。取得後に target の所有 token と使用保持を加え、runtime の提出保持へ引き渡す。取得待ちの間に caller の scope が終了しても参照先を失わず、取得・提出の失敗では受理済み work の有無に従って保持を返す。

target の再利用条件、同時進行する frame 数、backpressure と frame pacing は provider と host の presentation 接続が管理する。GPU completion だけで presentation の使用終了とみなさない。window／canvas の接続は host の責務であり、描画 library の signature に platform 固有型を含めない。

helper は target の取得後、queue の受理前に失敗した場合は Discard する。helper と frame は queue の受理時点で内部的に提出済みとする。その後に Present が失敗しても、Dispose が未提出 target として Discard しない。execution が caller に返らない場合も runtime／presentation 接続が GPU completion と target の返却条件を満たすまで所有を維持し、Present の失敗を呼出し元へ報告する。

## コード配置

以下は repository root 相対の目標配置とする。`Lumyte.Graphics.RenderGraph` は既存 project を改編し、下表の役割ごとに整理する。共通 assembly には Native／Portable の実装と Microsoft.Extensions への参照を追加しない。

| 配置先 | 内容 |
| --- | --- |
| `src/graphics/Lumyte.Graphics.RenderGraph/Contracts/` | IGpuRenderPassContract、宣言 context、入力の snapshot／保持契約。機能固有の request は置かない |
| `src/graphics/Lumyte.Graphics.RenderGraph/Graph/`、`Planning/` | GpuRenderGraph、logical resource／依存、Compile、plan と構造 cache |
| `src/graphics/Lumyte.Graphics.RenderGraph/Inputs/` | GpuGraphValue、型付き CPU／resource input、bindings builder と不変 bindings |
| `src/graphics/Lumyte.Graphics.RenderGraph/UploadData/` | 内容 key、画像／buffer／package の不変転送データと profile。file loader は置かない |
| `src/graphics/Lumyte.Graphics.RenderGraph/Resources/` | 共通 resource facade の契約、opaque ref、scope／pin の公開契約。物理資源と系統別対応表は各 provider に置く |
| `src/graphics/Lumyte.Graphics.RenderGraph/Runtime/` | IGpuRenderProvider／IGpuRenderRuntime、GpuRenderProviderRegistry、Options、execution と completion の共通契約 |
| `src/graphics/Lumyte.Graphics.RenderGraph/Presentation/` | GpuRenderContext、GpuFrame、presentation target、形状変更の例外と acquire／submit／present の共通 helper |
| `src/graphics/Lumyte.Graphics.RenderGraph.Tests/` | 新設予定の隣接 xUnit project。Contracts／Planning／Inputs／Ownership の CPU 試験を fake provider で実行する |
| `src/graphics/Lumyte.Graphics.RenderGraph.Tests/Conformance/` | 同じ consumer fixture を一度だけ build して両 provider で使う適合試験。実 GPU の実行は通常の CPU 試験から区分する |
| `benchmarks/Lumyte.Benchmarks/Graphics/RenderGraph/` | 既存 benchmark project 内の新設予定領域。plan 再利用、bindings 更新と CPU allocation の測定 |

現行 project の `RenderGraph/` にある共通契約と計画処理を整理して移し、物理配置、barrier、binding と command 記録は provider 側へ分ける。新しい公開契約を旧実装へ forwarding する互換層は作らない。`.Tests` は production project の隣に置き、実機適合試験に必要な provider の参照を production の共通 project へ逆流させない。

## 使用例

機能 library が `BlurPassContract.Instance`、`BlurPassRequest` と出力 `Color` を定義し、host が両系統の実装を登録済みとする。`runtime` は Hosting integration の非同期初期化後に借用する共通 runtime とし、描画 library は registry を操作しない。`imageData` は Lumyte.Resources が `images.sampled` version 1 に従って準備した `GpuPackageUploadData` で、"color" という線形 premultiplied RGBA の画像 export を持つ。ロード処理はこの例に含めない。

```csharp
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Passes;

using var scope = runtime.Resources.CreateScope();
var image = await scope.ImportPackageAsync(imageData, cancellationToken);

var graph = new GpuRenderGraph();
var source = graph.ImportTexture("source", image.GetTexture("color"));
var radius = graph.CreateInput("blur.radius", BlurRadiusInputContract.Instance, 3);
var blurred = graph.AddPass("blur", BlurPassContract.Instance, new BlurPassRequest(source, Radius: radius));
graph.ExportTexture(blurred.Color);
var plan = graph.Compile();

var bindings = plan.CreateBindings();
bindings.Set(radius, 5); // 次回以降は変更した入力だけ Set する。
using var execution = await runtime.SubmitAsync(plan, bindings.Build(), cancellationToken);
await execution.WaitForCompletionAsync(cancellationToken);
var result = execution.GetExportedTexture(blurred.Color);
// result を後続 graph で使う間は execution または別の pin を保持する。
```

BlurRadiusInputContract は半径の値をそのまま Snapshot し、Retain では何も保持しない。機能 library の `graph.AddBlurPass(...)` はこの AddPass を包む。利用側に shader、workgroup size、pipeline、root data、binding は現れない。新しい機能の作者は共通 contract に加え、Native／Portable の二本の pass 本体を提供する。

画面表示では、最初の description を既存の BeginFrameAsync で取得し、その未提出 frame を返してから持続 plan を作る。`render` は Hosting integration が用意した借用 context とする。以下の `BuildDisplayPlan` は application の CPU helper であり、新しい Graphics API ではない。内部で新しい graph に画像の import、半径 input、Blur、target input、表示変換と target への書込みを追加し、MarkOutput と Compile を行って `Plan`、`Target`、`Radius` と `Bindings` を返す。出力の encoding と alpha は application が選ぶ。GPU の状態から推測しない。

```csharp
GpuGraphTextureDescription targetDescription;
using (var probe = await render.BeginFrameAsync(cancellationToken))
    targetDescription = probe.TargetResource.Description;

var display = BuildDisplayPlan(image.GetTexture("color"), targetDescription);
while (!cancellationToken.IsCancellationRequested)
{
    display.Bindings.Set(display.Radius, currentRadius);
    try
    {
        using var displayed = await render.SubmitAsync(
            display.Plan, display.Bindings.Build(), display.Target, cancellationToken);
        // host が frame 数と pacing を制御し、Completion の失敗も監視する。
    }
    catch (GpuPresentationTargetChangedException changed)
    {
        // この試行は未提出。新しい graph の slot と bindings を一緒に作り直す。
        display = BuildDisplayPlan(image.GetTexture("color"), changed.Description);
    }
}
```

通常の frame は同じ plan を保ち、変更する入力だけ Set する。初回の probe は一度だけで、resize 時は実際の取得で返された description を使う。旧 plan の参照を手放しても、既に受理された execution は自身の bindings と GPU 使用保持で完了まで存続する。CPU 待機の取消しや device loss をこの catch で握りつぶさない。

## 所有、同期、失敗

この節を共通の規範とする。provider ADR は自系統の SPI と同期への接続、機能 ADR はデータや派生 cache に固有の依存だけを補足する。

### Integration と終了

Hosting integration から借用した runtime と描画 context は、その integration が一度だけ終了する。利用者は自分の scope、pin、execution と frame の保持を終了し、借用 runtime を Dispose しない。手動 composition の caller は自分で作った runtime と描画 context を同じ順序で終了する。いずれも runtime が構築・記録・GPU 利用を drain し、pass、manager、backend を終了してから、その生成時に借用した CPU 依存の所有元を解放する。

integration は利用側の停止と scope／pin 等の返却が runtime の管理資源の終了より先になるように接続する。停止時は既に渡した resource facade と scope を含めて新規操作の受理を閉じ、進行中の操作を drain する。保持の返却は停止後も許可する。利用側の DI container の最終破棄だけに外部保持の返却を任せない。

### 不変入力と使用保持

graph と plan は要求構造と初期 CPU snapshot、bindings は今回の CPU snapshot と所有情報を保持する。いずれも managed ref を持つだけで GPU 資源の生存を延長しない。caller は SubmitAsync 呼出しまで scope／pin と取得済み target を維持する。SubmitAsync は最初の await より前に今回の使用保持を取得する。以後は provider が resource、shader、pipeline 定義、内部一時資源と cache entry を GPU 使用終了まで維持する。例外や待機の取消しは使用終了を証明しない。

CPU snapshot は完全な値と明示的な所有集合を持ち、未変更の page／部分木を共有する。差分や変更索引は加速情報とし、初回、世代の飛び越し、巻戻し、別 runtime、cache 回収後もその snapshot だけから再構築できる。plan／bindings が保持する CPU 内容と、各 execution が保持する GPU 世代は別である。古い入力を外部の「最新版」にすり替えず、使われなくなった CPU／GPU 世代を cache へ無制限に保存しない。

同じ plan の複数 execution は、それぞれの bindings と内部実行 state を使う。transient は execution ごとの論理所有とし、GPU 使用が重なる実行で同じ書込み storage を無条件に共有しない。pool や ring の物理再利用は completion と内部依存に従う。export も execution に属し、plan の slot を「前回結果」で上書きしない。

各 BuildAsync が作る内部資源と使用は専用 graph に明示する。外部の Read／Write だけを見て、内部の一時 texture や pass 間 barrier を推測しない。provider は展開後の実際の first/last use を外部依存に接続して同期を計画する。共通の論理順序から GPU stage を固定しない。

BuildAsync の準備で先行提出する upload は、新しく作る private 資源・不変世代の初期化など、同じ plan の GPU pass に先行してよいものに限る。先行 pass が読む既存資源や共有 cache を後続機能が更新する場合、準備では staging と lease を用意し、更新を内部 graph の copy pass として依存に載せる。CPU 上の BuildAsync の呼出し順は GPU の実行順の代わりにならない。

### GPU 内容世代と提出結果

CPU template／packing の準備、queue の受理、GPU 使用終了と処理の成功を区別する。provider は pass が登録した GPU 内容世代を、その内容を作る内部 writer と提出結果へ結び付ける。cache が参照を持つだけでは内容の準備完了を意味しない。

| 時点・結果 | 内容の利用と保持 |
| --- | --- |
| 内部 writer の登録後、未受理 | 当該構築の内部依存としてのみ使える。他の実行へ転送済みとして公開しない。必要な writer の culling、後続 BuildAsync や最終 Submit の受理前失敗では世代を無効にし、構築・記録を停止して未提出記録を破棄してから保持を返す。 |
| writer が受理済み、結果未確定 | 後続実行は provider の世代取得 SPI を通してのみ共有する。writer の順序依存、成功・失敗の依存と GPU 使用保持をまとめて引き継ぐ。CPU 側で登録順を覚えるだけでは代用しない。 |
| GPU 使用終了、診断未確定 | 安全な回収の条件にはできるが、内容の成功公開には使わない。保持して共有する場合も結果依存を残す。 |
| writer と内容依存が成功 | その不変 GPU 内容を再利用できる。eviction と最後の GPU 使用終了後に回収する。 |
| 内容を保証できない失敗 | 世代を無効化し、依存する実行・派生世代へ失敗を伝える。受理済み work は取り消した扱いにせず、実際の使用終了または確定した device 停止まで保持する。 |

専用 provider の SPI は世代の登録と取得を担当する。使用保持の Retain／Dispose から受理・成功を推定せず、pass の cache を非同期 callback で直接書き換える必要もない。受理済みの private 事前 upload も同じ結果依存を持つ。既存の内容を同じ storage で更新する場合は、旧 reader の順序を守るだけでなく、旧世代を将来の再提出へ誤って返さないようにする。差分の基底が失われた場合は完全な CPU snapshot から再構築する。

成功は backend が報告する診断と内容依存に基づく。GPU の計算内容の正しさを独自に検証するという意味ではない。Native は native API の失敗・device loss、Portable は非同期 validation／pipeline 診断も completion へ接続する。queue の到達だけでは処理成功を確定しない。無関係な過去の提出失敗を以後の全実行に伝播させず、内容の依存を引き継いだ実行だけを失敗させる。

共通 package と export の取得は成功確認後とする。内部 cache で未確定の世代を使う経路は provider の結果依存に限定し、未確定の共通 ref を利用者へ渡す経路を増やさない。presentation 接続へ渡した completion も成功と失敗を保持し、失敗した表示の診断を host へ伝える。既に提出した GPU work や表示を後から取り消せるとは扱わない。

取消しは最終提出の受理前に扱い、受理後は execution を返す。待機の取消しは GPU work の取消しではない。準備中の upload を含め一部の work が既に受理されてから失敗した場合も、runtime がその使用終了まで保持を引き受ける。device loss を正常 completion とみなさない。

### 資源境界と低レベル操作

所有者が管理する import/export 資源の生成用途と entry／exit state は provider の内部契約とする。transient の用途は展開後の使用から決め、永続資源はその uploader／owner の生成契約を引き継ぐ。後続機能からの使用もその資源の対応範囲内で行う。未知の外部 raw resource は専用 interop で状態、先行 completion、所有の受渡しを明示する。

共通 ref は別 runtime に移送しない。実装を切り替える場合は同じ不変の転送データを新しい runtime へ upload し、その runtime の参照を resource input へ設定する。固定 import を使った構造は作り直す。export の後続使用は新しい pin／execution が保持を取得してから元の execution を終了する。

root は各実装が自分の生成構造体で直接渡し、buffer fallback を行わない。Parameter Data が必要な pass は、その実装が明示的に資源と転送を用意し、shader が root から参照・算出する。低レベル command に Parameter Data の生成・upload・所有を追加しない。

## 同じ binary の条件、検証と cache

共通 contract、CPU 実行環境と version が互換であり、利用する機能について両系統の pass 実装と shader artifact が配布されていることを条件とする。未登録実装や契約版の不一致は提出準備前に報告する。片方の GPU code を自動変換して対応済みとは扱わない。provider の Auto 選択は初期化時に行い、提出途中の切替えはしない。

AOT でも実装を明示登録して選択できる。runtime での C# compilation は必須にしない。shader の native pipeline 生成や graph の Compile は consumer assembly の再コンパイルとは異なる。OS／CPU／WebAssembly 間の executable 変換を保証するものではない。

共通 graph が確認するのは機能契約の ID／版／要求型、宣言した依存、循環、初期化、culling、export、plan と入力 slot の対応、UseDeclared の参照集合、資源 slot の設定・形状と意図しない alias、共通 ref の runtime／世代など、自分の契約である。各実装は同じ外部意味を守る責任を持ち、shader の内容を解析して意味の一致を証明する validator は作らない。format、usage、binding、pipeline、GPU 同期の合法性は native API／WebGPU runtime／compiler の診断に委ねる。

同じ plan の反復提出は論理 cache の照合を必要としない。論理 cache は構造変更時の再 Compile に用い、契約 ID／版と宣言構造を共有する。今回の bindings と import は実行ごとに保持し、cache に過去フレームの snapshot を無制限に残さない。

provider は内部 graph の template、保持情報、GPU 部品、batch と派生結果を依存する世代に応じて再利用する。未変更の全要素を毎回コピー・hash・再列挙・再転送することを通常経路の要件にしない。物理 cache と shader／GPU data の cache は実装、device、資源世代に束縛する。差分履歴の欠落、cache eviction、古い snapshot の再提出は完全な入力から再構築できる。Native の内部 graph や shader 入力を Portable に流用せず、Resources と二重に同じ GPU 資源を所有しない。

これは必要な描画処理を省略する保証ではない。camera の変更は culling や透明 sort、内容や大きさの変更は内部資源計画へ影響する。GPU command の再記録と再利用の選択は各系統に委ね、WebGPU の command を再提出できることや常に一定時間で処理できることを共通要件にしない。

## 採用範囲と未実装事項

共通化する対象は機能要求、外部入出力、再利用できる graph 構造、型付きフレーム入力、runtime 選択と実行結果である。pass 本体、shader、GPU data と低レベル操作は二系統で実装する。

転送データ型、単一の公開 contract、input contract と所有情報の共有、不変 bindings、resource input、plan の反復提出、差分準備、機能 registry、非同期準備と提出、共通 resource facade、二 provider と再利用 plan の presentation 接続、Hosting integration との登録 snapshot・所有の接続は未実装である。今回定めた GPU 内容世代の結果依存、使用終了と成功の区別、成功後の export 公開、target 形状変更の通知も実装完了を示さない。Lumyte.Resources の loader／decoder／評価 API はこの ADR では定義しない。適合試験では一度ビルドした同じ consumer assembly を両 provider へ接続し、機能の出力、依存、所有、失敗と shader 管理が利用側に漏れないことを確認する。初回表示、resize の競合、未提出 target の返却、遅延診断と出力の公開はそれぞれ fake で検証する。GPU command 列の一致や provider ごとの consumer 再ビルドを成功条件にしない。
