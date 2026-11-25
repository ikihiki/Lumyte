# ADR-GRAPHICS-0040: 汎用の型付きパイプラインを Renderer2D に直接渡す

- 状態: 採用
- 日付: 2026-11-17
- 更新日: 2026-11-21
- 基準コミット: `fcde70b3c84a620ab776ca19982ff678fe142315`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別 interface と DI](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)
- 変更対象: 汎用 DAG の構築境界と、構築済み pipeline を Renderer2D へ直接渡す constructor を追加する。
- 継続する判断: Canvas の記録、既存の命令別 interface、clip の数値規則、描画順、準備結果と資源所有権を維持する。

## 背景

処理の作者は、CPU の加工、GPU 計算、転送、複数入力の合流を型付きの入出力として組み立て、描画以外にも再利用したい。描画専用の graph から外側の renderer を生成するだけでは、この再利用と、構築した処理を既存 Renderer2D に直接渡す操作を分離できない。

基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の PrepareAsync は DrawingCommandRenderers2D の 11 種別に配送し、一つの引数計画を確定する。同 assembly の `Execution/Renderer2D.Retained.cs` の TakeBuiltinBatch と `Rendering/PreparedRecordBatch2D.cs` の PrepareAsync は、種別をまたぐ連続命令を一括処理する。独立 coverage prepass を持つ構造ではない。

同じ基準の `Execution/PreparedDraw2D.cs` の Record は同期的な一回の記録である。`src/Graphics/Lumyte.Graphics.Abstractions/IGraphicsQueue.cs` の Submit、`IGraphicsSubmission.cs` の WaitAsync、`IGraphicsBuffer.cs` の MapAsync と CopyTo は別操作であり、準備は送信しない。

## 決定

Core に描画を知らない型付き Stage/Then/DAG、`PipelineDefinition<TInput,TOutput>` と構築済み `IPipeline<TInput,TOutput>` を置く。Graphics 拡張が Device 配置と GPU 実行を、Renderer2D 内の binding が唯一の 2D 結合を担い、構築済みの汎用 IPipeline を Renderer2D の constructor へ直接渡す。グローバル登録、service locator、型による Resolve、暗黙の既定 pipelineは設けない。

### 利用者の変更前・変更後

変更前は固定配送表を直接渡す。device、list、target、ct は caller が保持する実在 API の例である。

~~~csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, ct);
// Record → Finish → Submit → 実完了 → command.Dispose → prepared.Dispose。
~~~

変更後は次の提案 API を使う。外側の IRenderer2D は描画利用者向けの interface であり、Renderer2D は構築済みの汎用 pipeline を直接借用する。専用の公開 strategy interface や wrapper への変換は挟まない。基準コミットに対する IRenderer2D の追加は後掲の差分に示す。通常の描画利用者は引き続き IRenderer2D.PrepareAsync と既存の prepared owner を使う。

以下の limits、samples、policy、routes は caller が明示する不変設定である。例えば limits は contour=2048、展開 segment=65536、stroke triangle=262144、tile reference=1048576、samples は Count=16/GridVersion=1、policy は scratch=64MiB、transfer=128MiB、readback=64MiB、epoch=8、in-flight=3、asset=512/64MiB、ArgumentTables=Auto とする。全 11 route の受入れ可否も個別に指定する。artifacts は完成済み shader/pass の provider、execution は借用 Lumyte.Execution の実行環境、resourceOwner は成功時だけ移管する所有者であり、初期化を例から省略している。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

var g = new PipelineGraph<RenderRequest2D>();
// request は CPU と device の参照を併せ持つ束。全体を Upload しない。
var commands = g.Stage(RenderFields2D.Commands(), g.Input);
var manifest = g.Stage(RenderFields2D.ResourceManifest(), g.Input);
var frame = g.Stage(RenderFields2D.Frame(), g.Input);
var views = g.Stage(RenderFields2D.ResourceViews(), g.Input);
var targetPort = g.Stage(RenderFields2D.Target(), g.Input);
var scene = g.Stage(CpuStages2D.Normalize(limits), commands, manifest, frame)
    .Then(Transfers.Upload<SceneV1>());
var deviceFrame = frame.Then(Transfers.Upload<FrameV1>());
var coverage = g.Stage(ComputeStages2D.Coverage(samples), scene, deviceFrame);
var paint = g.Stage(ComputeStages2D.Paint(samples), scene, views, deviceFrame);
var clip = g.Stage(ComputeStages2D.Clip(samples), scene, deviceFrame);
var output = g.Stage(RasterStages2D.Compose(samples),
    scene, coverage, paint, clip, targetPort);
PipelineDefinition<RenderRequest2D, Device<TargetWritesV1>> definition = g.Complete(output);

using var build = new GraphicsPipelineBuildHost(execution, device, artifacts, policy.Execution);
var description = definition.DescribeDataflow(); // 同じ不変 node/edge。実行や確保なし。
using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(definition);
using IRenderer2D renderer = new Renderer2D(device, pipeline, routes, policy,
    resourceOwner: resourceOwner, validateResources: validateResources);
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, ct);
// 変更前と同じ記録・送信・実完了・破棄。逆順 Dispose は全使用 lease 返却後。
~~~

RenderRequest2D は Renderer2D 内部 binding が context、命令列と Renderer2D の内部 source binding から作る不変の一回分の束である。RenderFields2D はそれぞれ `Host<CommandsV1>`、`Host<ResourceManifestV1>`、`Host<FrameV1>`、`Device<ResourceViewsV1>`、`Device<TargetV1>` を返す純粋な抽出 stage で、device view/target は元の所有 lease と generation を保持したまま束縛する。これらは転送ではない。CPU 由来の規範配列だけを明示 Upload し、device 参照や request 全体をシリアライズしない。Core は束のフィールドや 2D 意味を知らない。

融合版では上の coverage、paint、clip、Compose の四 node を次の一 node に交換する。Complete、Build、constructor の順は同じである。

~~~csharp
var output = g.Stage(RasterStages2D.EvaluateAndComposite(samples),
    scene, views, deviceFrame, targetPort);
var definition = g.Complete(output);
~~~

これは完成済み shader/pass を持つ RasterEvaluateAndComposite2D という具体型の選択である。独立版は別 pass と中間保存を持ち、融合版は中間 buffer を公開しない。融合 IR、hint、実行時最適化、shader 自動生成は設けず、選択済みトポロジーを実行中に変えない。Mesh の具体型への交換も同じ構文と数値契約を使い、非対応 device は構築前に拒否する。

### 描画に依存しない利用例と既存基盤の再利用

CPU 専用の例では画像・device・2D schema を一切使わない。以下の Parse/Normalize/Summarize/Format は例のアプリケーションが実装する型付き stage、TextInput/Rows/Summary/Report はその不変 payload である。既存にはこの typed payload DAG はなく、変更前は caller が順に処理して中間値と寿命を管理する。変更後は同じ Core の構築を、別の外側の adapter から直接利用できる。

~~~csharp
using Lumyte.Pipelines;

var g = new PipelineGraph<TextInput>();
var rows = g.Stage(new ParseRows(maxRows: 10000), g.Input)
    .Then(new NormalizeRows());
var summary = g.Stage(new SummarizeRows(), rows);
var report = g.Stage(new FormatReport(), rows, summary);
PipelineDefinition<TextInput, Report> definition = g.Complete(report);
using var build = new PipelineBuildHost(execution, cpuBudget);
using IPipeline<TextInput, Report> pipeline = build.Build(definition);
using var exporter = new ReportExporter(pipeline); // 明示的な借用。登録しない。
using PipelineResult<Report> result = await exporter.ExportAsync(textInput, ct);
// ReportExporter.ExportAsync は pipeline.PrepareAsync(input, ct) を呼ぶだけ。
// CPU 専用なので返却時の Value は完成済み。result が payload の所有権を保持。
~~~

新しい Core の assembly は Lumyte.Pipelines とし、Lumyte.Execution と共通メモリ基盤にだけ依存し、Graphics/Device/2D を参照しない。新しい Lumyte.Graphics.Pipelines assembly が Core と Graphics.Abstractions を参照する。Lumyte.Graphics.Drawing2D.Pipelines は新しい project ではなく、既存 Lumyte.Graphics.Drawing2D assembly 内の namespace とする。RenderRequest2D、RenderFields2D、Renderer2D 内の pipeline binding と source binding は同 assembly に置き、Drawing2D assembly が Core と Graphics.Pipelines に依存する。Graphics.Pipelines から Drawing2D、Core から Graphics への逆参照は作らない。Core の schema は version、layout とアプリケーション固有の不変 metadata を保持し、sample/grid/色などの解釈は 2D binding で追加する。

基準コミットの `src/Core/Lumyte.Execution/JobGraphBuilder.cs` の Add は `WorkKey<TContext,Unit>`、AddRange は `RangeWorkKey<TContext>` を取り、DependsOn と Build が順序と循環を検査する。`ExecutionRuntime.Graphs.cs` の Schedule は共通 struct context の graph を有限資源で受け入れる。この CPU scheduler、owner placement、range 分割、admission/cancellation を再利用し、別の thread pool/scheduler を新設しない。新しい lowering adapter は一回分の lease table を参照する struct context と Unit handler を生成し、型付き port の値・共有 producer・schema 検査・lease の公開をその table に載せる。JobGraph 自体が typed payload を辺に流したり GPU dependency を管理したりするという意味ではない。CPU 区間を既存 runtime へ投入し、GPU 区間の native 記録・送信は Graphics host が所有する。

同じ基準の `src/Core/Lumyte.Resources/IResourceTransform.Typed.cs` の `IResourceTransform<TInput,TOutput,TRuntime>` は runtime を検査して借用入力から所有出力を構築し、ReleaseAsync で元 runtime 上の破棄を行う。`ResourceLease.cs` の `ResourceLease<T>` は一つの revision と runtime generation を保持する。artifact や不変入力を既存 ResourceManager から得る場合、この revision lease を provider の owner として保持し、generation 検査と release を再利用する。一方、Resources の address 単位の構築・更新は frame 内の任意 fan-in/fan-out、record-only GPU 出力、submit receipt を持つ実行ではないため、resource transform の名前を変えて代用せず、frame scratch や execution ticket を resource cache に格納しない。

### 型付き接続と寿命の共通契約

Stage と Then は同じ node 追加であり、Then は直前の port を一入力 stage へ接続するだけである。各呼出しは別 node、同じ port の再利用は fan-out、複数引数は fan-in となる。同じ producer は一回の準備で一回だけ評価し、同型 node の自動重複排除はしない。入力の型、所有 graph、null、凍結状態、循環、欠落入力を検査する。Complete は任意の `Port<TOutput>` を受け、実装設定と配線を不変にコピーして graph を凍結する。route、TargetWritesV1、Device、予算を Core の Complete の必須引数にしない。

Port は受動的な辺で、実データ・native owner・待機能力を持たない。schema と有限予算を build/per-run で検証してから確保し、実行中の値は別の lease が保持する。Core の `PipelineResult<T>` は出力と実行状態の所有者であり、CPU 出力は完成後だけ Value を読める。Device 出力は記録可能な依存計画の準備完了を表し、GPU の実完了ではない。Graphics の readiness metadata がこの違いを型と plan に保持し、CPU-readable を偽装しない。

一つの準備は入力、frame、資源 generation を不変捕捉し、共有評価はその一回に限定する。別 frame の writable scratch、途中結果、未完了 submission を流用しない。互換な不変 geometry と内部サービスの構築内共有は設定・実装 version・device・owner/generation を含む意味 identity で行い、型だけや global cache では統合しない。

宣言は device・scratch・Dispose を持たず、同じ定義から複数 build が可能である。build host は未公開 processor/artifact と失敗 cleanup を所有し、成功した compiled pipeline は host から独立した所有権を持つ。Renderer2D は compiled pipeline を直接借用し、constructor 成功時に独立した使用 lease を取得する。失敗時は取得済み lease を返し、caller の pipeline の所有権を移管しない。各層は使用中の下位 owner に使用 lease を取り、Dispose は新規準備を止めるが、prepared/frame/execution の lease が返るまで native owner を回収しない。caller は逆依存順で解放し、借用 device・runtime・context を最後まで保持する。失敗時は逆順回収し、失敗した owner の retry を host または生存 owner に残す。resourceOwner は Renderer2D 構築成功時だけ移管し、失敗時は caller に残る。

Graphics の `Host<S>`/`Device<S>` は同じ schema の配置であり、Upload/Download は通常の明示 stage である。Graphics の `Passthrough<S>` は既存 device lease を別の所有 lease として転送する通常の設定であり、コピー・submit・完了待機や 2D route を追加しない。異なる表現は別 schema と明示 conversion を必要とする。転送 helper は同形の Stage を追加するだけである。構築 helper は同じ graph に port を追加・返すだけで、renderer、サービス cache、隠れた送信 loop を作らない。

### Graphics と 2D binding の schema・数値・予算

本例の schema は次の内容を固定する。名前だけを一致させず、各 schema の記述子が version、element layout、座標系、色と alpha、sample 数と順序、grid、source identity の規約を持つ。

- `CommandsV1`: 不変の論理命令列。命令 ordinal、状態、transform、paint、clip、資源参照を含む。
- `ResourceManifestV1` / `ResourceViewsV1`: 同じ資源 snapshot の CPU 記述と device view の別 schema。所有者・世代・image sampling 条件を対応づける。これらは同一 schema の転送ではない。
- `FrameV1`: target extent、pixel scale、座標変換、固定 16 sample の位置・順序、frame source identity。
- `SceneV1`: 正規化 geometry、paint 記述、CPU で確定した clip の分類・scissor・16 bit mask、論理 ordinal と展開 ordinal。stroke の width/cap/join/miter と変換結果を含む不変の有限配列。Host と Device は同一の規範 layout を使う。
- `Coverage16V1` / `Paint16V1` / `Clip16V1`: 同じ scene 命令と pixel grid に対応する 16 sample の coverage、線形 premultiplied color、clip 値。単一の平均 coverage に退化させない。
- `TargetV1`: 初期 target view と load/clear 条件。`TargetWritesV1`: 同じ target への順序付き書込みを完了させる依存出力であり、CPU-readable な画像ではない。

Host/Device の型一致に加え、構築時に固定 schema 記述子を照合し、各 Prepare で extent、sample/grid、scene source identity、資源世代を照合する。別 scene の coverage と paint、異なる version、失効した view は確保・記録前に拒否する。`Host<S>` が CPU array、`Device<S>` が任意の backend buffer というだけでは互換としない。Device の buffer usage、alignment、binding layout、texture format も実装の要求と照合する。

`Port` の再利用が fan-out、複数引数が fan-in である。graph 外の port、欠落入力、循環を拒否し、文字列 registry や stage 順序 enum で配線しない。実装の種類は任意のトポロジーで交換できるが、意味・配置の型を破る順序変更はできない。target の書込みは SSA の依存鎖として表し、同じ target version を二つの書込み node が消費する未順序の競合も拒否する。

### 数値規則と stage の意味

[GRAPHICS-0033 の数値規則と 16 サンプル相関](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)を維持する。命令 j の sample s で geometry coverage、paint、clip を相関させ、`q[j] = sum(s = 0..15, coverage[j,s] * clip[j,s] * paint[j,s]) / 16` とし、その命令の `q[j]` を painter order で既存 destination に SrcOver する。命令ごとの平均の後に合成し、全命令を sample ごとに合成してから平均する方式へ変更しない。

Normalize は非表示命令も含む順序と clip 境界を捕捉する。Coverage は同じ座標・sample 規則で形状を評価し、Paint は paint、transform、image sampling を評価し、Clip は CPU で確定した分類・scissor・16 bit mask を同じ sample 順に展開する。clip の幾何 predicate を GPU で再計算せず、既存の CPU clip 正規化とその予算を維持する。Compose は scene の ordinal を使って三出力を結合し、平均と SrcOver を行う。融合型は同じ演算規則を shader 内で完結する。中間表現の丸めや backend の predicate 差は実装適合性として検証し、既存の許容差を無条件に広げない。

独立 pass の分解は今回の設計であり、基準コードの内部構造を説明したものではない。CPU・Compute・Raster・Mesh は実装手段であって数値意味を変えない。Mesh stage も同じ schema の Device port を用い、mesh shader 非対応なら構築時に拒否する。

非二値の mesh coverage は、pixel center を論理空間へ逆変換して頂点 coverage を barycentric 補間し、saturate 後に重なる triangle の最大値を採る既存規則を維持する。その一つの center coverage を同じ pixel の16 sample へ展開し、各 sample の paint と clip との相関を評価する。sample 位置ごとに mesh coverage を再補間しない。根拠は基準コミットの同 assembly の `Shaders/drawing-fragment.slang` の `evaluate` と `meshCoverage`、`Shaders/draw-records-fragment.slang` の `shapeCoverage` と `meshCoverage` である。

予算は展開後の geometry、tile/bin/scan、16 sample 中間、転送、readback、同時実行数の peak live bytes を含む。stroke、cap、join、miter、退化形状、clip 展開を含めて checked arithmetic で有限上限を導き、超過は確保前に拒否する。無制限の retry、暗黙の品質低下、精度不足の clamp はしない。宣言予算と backend capability、usage、format、owner context、転送・readback 対応を全体検査してから資源を確保する。実確保や device failure はエラーであり、別アルゴリズムへ fallback しない。

実行時の内部 lease は port と別物であり、通常の公開描画 API に stage runner を露出しない。`ValueLease<Host<S>>` は CPU callback 中に使用可能な不変データ、`ValueLease<Device<S>>` は資源世代と producer dependency を保持する。Device lease を取得しても GPU 完了を意味せず、scheduler だけが barrier と依存 submission を接続する。Download 後の Host lease は実完了と mapping/copy の成功後にだけ公開し、callback 終了後に保持するには別の所有 lease が必要となる。外部実装も owner を独断で破棄したり port を runtime handle として解決したりできない。

### 直接渡す pipeline の結合と描画順

Renderer2D の追加 constructor は `IPipeline<RenderRequest2D, Device<TargetWritesV1>>` と routes/policy を受け、その instance を直接借用する。constructor は Graphics build に由来する正確な pipeline identity、device reference/generation、入力・出力 schema/version/layout、target 書込み endpoint、readiness 契約を検証する。routes/policy を不変捕捉し、route の完全性と pipeline の受入れ契約、build に固定した有限予算・capability と policy の整合性を検査してから使用 lease を取得する。不一致を constructor 成功前に拒否し、policy で compiled pipeline を再構築・書換え・暗黙補完しない。

Renderer2D の内部 binding は RenderPreparationContext2D と不変の連続命令列を RenderRequest2D に束ね、pipeline.PrepareAsync の準備結果を既存 IPreparedCommandBatch2D に包む。RenderPreparationContext2D の内部 source binding lease から target view、資源 manifest/view、frame/source identity を同 assembly 内で渡し、外部に ledger の組立てを要求しない。Renderer2D は引数計画、資源 coordinator と入力 snapshot を一回だけ所有する。内部 binding/bridge は公開 wrapper ではなく、stage ごとの new Renderer2D や型別サービス cache を作らない。

Renderer2D に束縛した CommandRoutes2D は一つの instance に閉じた受入れ表で、FillRect、FillRoundedRect、FillCircle、DrawLine、FillPath、StrokePath、DrawImage、DrawCoverageMask、DrawDistanceField、DrawMesh、DrawPaintGraph を個別に指定する。全体列を一回の pipeline 入力にする方式が基本である。旧 DrawingCommandRenderers2D constructor は互換用の local adapter に包み、既存の連続した同種 batching を維持する。種別による全列の再収集や、将来の命令を先取りして一つの group に集めることはしない。全体列でも layer/effect、backdrop、capture、target 変更の境界は跨がず、論理 ordinal と展開 ordinal の painter order を保つ。対応不能の純粋な事前拒否だけを認め、実行失敗後に別候補へ切り替えない。

Canvas は論理記録だけを行う。[GRAPHICS-0022 の決定](GRAPHICS-0022-2d-render-graph-layers.md#決定)の layer/effect と backdrop を維持し、内部 RenderGraph は barrier、資源依存、transient allocation に再利用する。retained/Graph は同じ直接借用 pipeline、資源 ledger、引数計画と source binding を借用する。Drawing2D assembly の内部 bridge を friend-access で Drawing2D.RenderGraph へ公開し、元 renderer の pipeline 使用 lease と completion 通知を渡す。単なる interface 型変更だけではなく、typed command、layer、backdrop、capture、失敗した Append の owner まで同じ binding を保持する。

Graph 接続は bridge 対応、device、no-readback を Graph 変更前に検証し、未対応の外部 IRenderer2D を拒否する。外部作者の pipeline はこの内部 bridge を実装する必要はなく、constructor の適合性検証を経て Renderer2D を通して接続する。元 renderer と借用 Graph host の準備は直列化する。retained の Record、Finish、Submit、AttachSubmission、実完了、command 破棄、Retire、Dispose と未送信 abort、受付不明時の隔離を維持する。直接経路の prepared は実完了・command 破棄まで caller が保持し、Graph の culling は未記録と実行完了を区別する。

### GPU から CPU へ戻る区間実行

readback がない構成は通常の IRenderer2D.PrepareAsync を保つ。readback を含む場合は constructor が検証した汎用 pipeline の実行 metadata に基づき、外側の IAsyncPipelineRenderer2D.PrepareExecutionAsync からだけ利用する。Renderer2D 内部 binding が PipelineResult から汎用 AcquireExecution と同じ bridge で GraphicsPipelineExecution を取得し、PreparedPipelineExecution2D に包む。専用 strategy の capability dispatch は行わない。その構成の通常 PrepareAsync は確保前に NotSupportedException とし、Prepare/Record 内の隠れた Submit、wait、mapping を禁止する。

前例の coverage を次に交換すると、CPU → GPU → CPU → GPU となる。他の明示 inputs、paint/clip、target と Build/constructor は同じである。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

var downloaded = g.Stage(ComputeStages2D.Coverage(samples), scene, deviceFrame)
    .Then(Transfers.Download<Coverage16V1>());
var coverage = g.Stage(new FiniteCoverageCheck(16L * 1048576), downloaded)
    .Then(Transfers.Upload<Coverage16V1>(UploadRetention.Fresh));
// Compose → Complete → Graphics build → new Renderer2D は前例と同じ。
var segmented = (IAsyncPipelineRenderer2D)renderer;
using PreparedPipelineExecution2D executionPlan =
    await segmented.PrepareExecutionAsync(list, target, ct);
// executionHost は caller が保持する長寿命の GraphicsPipelineExecutionHost。
// new GraphicsPipelineExecutionHost(pipeline, ownerContext, retirementAuthority)。
try { await executionHost.ExecuteAsync(executionPlan.Execution, ct); }
finally { await executionHost.DrainAsync(); } // 失敗時も host を保持し再試行する。
~~~

PrepareExecutionAsync は不変捕捉、検証、最初の CPU 区間までを行い、送信しない。GraphicsPipelineExecutionHost は次の区間を record-only callback で自分の command に一回だけ記録し、readback copy もそこへ記録する。plan identity、device generation、epoch、正確な command identity を組にした一回限りの ticket を発行し、host 自身が Finish/Submit して receipt を同じ epoch に登録する。任意の submission の後付けや ticket の二重利用は許さない。

host は実完了を待ち、caller の owner-context dispatcher に戻り MapAsync、CopyTo、Unmap を行う。非同期 mapping 自体の完了と所有 CPU 配列への copy が成功して初めて Host lease を公開し、CPU continuation を評価して次の upload/GPU 区間に進む。成功した ExecuteAsync は最終 GPU 実完了と command 回収までを意味する。prepare の Device 出力 readiness とこの実完了を混同しない。

送信受付と receipt 登録の間で cancellation を観測しない。待機取消しは GPU cancellation ではなく、以後の CPU continuation/追加送信を止めても受付済み区間と mapping の進行・回収は host が保持する。確認済み未送信だけ command 破棄後に abort できる。Submit 例外で受付不明なら全依存 owner を隔離し、次節の実完了または退役証明なしに解放しない。DrainAsync は取消し不能で cleanup retry を保持し、失敗後も host/context/execution owner を捨てない。Dispose は暗黙 wait せず未完了を拒否する。主処理と cleanup の両失敗を保持して報告する。

### Graphics 単独の準備結果から実行計画を借用する

GraphicsPipelineResultExtensions は Core の PipelineResult から Graphics build の内部 readiness と所有権を検証して、record-only plan または区間 execution の独立した使用 lease を取得する。どちらも result の下位所有者を保持するため、result を先に返しても plan の使用中に回収しない。non-GPU、別 pipeline identity、別 owner context、失効世代、返却済み result、他の取得で既に消費した結果を確保・記録前に拒否する。取得は同じ一回分の排他的な実行権を消費し、二つの計画で二重記録できない。

owner context は当該 pipeline の Graphics build に結合した device/owner affinity に適合することを検証し、最初の取得でその準備結果に固定する。AcquireRecordPlan は途中の readback/CPU 再開が必要なら NotSupportedException とし、Record は一回の記録だけで送信・待機を隠さない。AcquireExecution は segmented execution を返し、前節の host だけが送信と完了確認を行う。host は取得時に固定した identity/context/generation との一致も受付前に検証する。Renderer2D の prepared owner への内部結合もこの同じ bridge を使用し、専用の別実行機構を持たない。

次は Drawing2D を参照しない Graphics 利用例である。gpuPipeline は caller が GraphicsPipelineBuildHost で明示的に構築した CPU/GPU の定義で、request はその入力である。host と ownerContext は呼出しより長く保持し、失敗した Drain も再試行する。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Pipelines;

using var result = await gpuPipeline.PrepareAsync(request, ct);
using GraphicsPipelineExecution run = result.AcquireExecution(gpuPipeline, ownerContext);
try { await host.ExecuteAsync(run, ct); }
finally { await host.DrainAsync(); }
// host = new GraphicsPipelineExecutionHost(gpuPipeline, ownerContext, retirementAuthority)。
// no-readback の直接記録なら、上の AcquireExecution/host を次に交換する。
// using var plan = result.AcquireRecordPlan(gpuPipeline, ownerContext);
// plan.Record(commands); → caller が Finish/Submit/実完了/command.Dispose → plan.Dispose。
~~~

### 論理 pass として既存 RenderGraph に参加する

GPU plan の作者は attachment や Load/Store、Begin/End を外側で管理する scope を作らず、一つの論理的な記録単位を RenderGraph の依存関係へ接続する。基準コミットの `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraph.cs` の `AddPass(name, Action<RenderGraphContext>)` と `RenderGraphPass.cs` の UseTexture/UseBuffer/DependsOn/KeepAlive をそのまま基礎にする。依存関係が決めるのは callback の実行順と GPU barrier であり、GPU 完了や時刻・所要時間の保証ではない。

変更前は caller が既存 API で callback と全 resource use を別に宣言する。次の recordWork は caller が用意した既存の record-only 処理で、target/vertices は既に import 済みの graph resource、scope は実際の全 access の集合である。所有者を graph の寿命へ結びつける作業は caller に残る。

~~~csharp
using Lumyte.Graphics.RenderGraph;

RenderGraphPass pass = graph.AddPass("work", context => recordWork(context.Commands))
    .UseTexture(target, targetState, targetScope)
    .UseBuffer(vertices, vertexScope)
    .DependsOn(previous);
// imported output への write は既存規則で root。その他の副作用なら pass.KeepAlive()。
~~~

変更後は no-readback の GraphicsPipelineRecordPlan と、その全外部資源に対する明示 binding を渡す。plan は前節の AcquireRecordPlan で得た caller 所有の借用元である。slot は plan.Resources.GetSlot(index) で取得し、捕捉された schema と access manifest に照合する。targetSlot/vertexSlot は plan の不変 manifest にある opaque key、targetTexture/vertexBuffer は pipeline request に渡したものと同一の native allocation であり、caller が同じ graph に import した target/vertices に対応する。scope/state は manifest と照合し、単に例の値を信用しない。

~~~csharp
using Lumyte.Graphics.Pipelines.RenderGraph;

var bindings = new PipelineGraphBindings(
    new[] { new PipelineGraphTextureBinding(targetSlot, target, targetTexture,
        targetState, targetScope) },
    new[] { PipelineGraphBufferBinding.Create(vertexSlot, vertices, vertexBuffer, vertexScope) });
RenderGraphPass pass = graph.AddPipelinePass("work", plan, bindings).DependsOn(previous);
// 非 output の独立した副作用を残す場合だけ pass.KeepAlive()。
// graph.Compile → compiled.Record(commands) → caller の Finish/Submit → 実完了
// → commands.Dispose → compiled.Dispose。未記録/cull は送信せず、保持 lease は compiled.Dispose で返す。
~~~

AddPipelinePass は新しい Lumyte.Graphics.Pipelines.RenderGraph assembly に置き、Core、Graphics.Pipelines、Graphics.RenderGraph にだけ依存する。Drawing2D を参照せず、Renderer2D への直接 pipeline 受渡しとも独立した汎用 adapter とする。外部の仕事を opaque plan の stage 間へ割り込ませる機能ではない。必要なら pipeline 作者が明示した境界で複数の plan を作り、それぞれを別 pass にして依存を結ぶ。

### Graph に渡す資源と状態の境界

plan は外部 texture/buffer の完全な不変 manifest と、slot ごとの native allocation/view/range、device generation、read/write scope、要求状態、終了状態を準備時に捕捉する。binding はその全 slot をちょうど一度対応づける。初期対応は既に import された texture と unmanaged typed buffer に限定し、未実体化 transient、外部 shader-data/sampler を含む plan は専用の完全 binding がない限り拒否する。非対応の種類を隠れた外部資源として通さない。artifact とそれだけが参照する不変 native owner も plan が lease を保持し、graph と共有する資源は manifest から省かない。

基準の RenderGraphPass.AddUse は一つの allocation に一つの TextureState を持ち、異なる状態の重複 use を拒否する。entry/exit 別状態を既存 graph が扱えるとはしない。初期 adapter は、外部 texture の全 stage が同じ宣言状態で使え、最後もその状態に戻る plan だけを許し、複数状態が必要な plan は graph 変更前に拒否する。必要な構成は作者が明示的に別 plan/pass へ分割する。graph は pass 間の barrier を、plan は内部 scratch と同一状態内の必要な memory barrier を所有する。buffer scope は内部の copy/shader を含む全 access の集合とし、終了時に宣言から外れる状態・access を残さない。内部 scratch は pass-local の保持資源とし、peak live bytes を pipeline 予算に計上し、外部出力扱いや culling root にしない。

AddPipelinePass は graph が building であること、name、device/世代、plan readiness、全 slot の不足・余分・重複、allocation/view/range の実 identity と imported handle の対応、alias ごとの state/scope 統合、local 所有者と有限予算を事前検証する。alias は名前でなく allocation identity で検出し、競合宣言や偽の別 allocation、別 graph、失効世代を拒否する。すべての preflight は graph の変更と plan の排他的記録権の claim より前に行う。成功時だけ一つの pass、use と保持 lease を原子的に追加し、記録権を graph に割り当てる。append 失敗は追加 pass/use/lifetime を rollback して claim を戻し、元 plan を再利用可能に保つ。同じ plan の二重登録・直接 Record との競合は拒否する。caller の plan/pipeline を Dispose しない。

callback は active render/compute encoder の外側で plan の記録だけを行い、plan 自身が開いた encoder を例外時も閉じる。Submit、Finish、wait、readback mapping、隠れた CPU 再開は禁止する。途中 readback は AddPipelinePass の preflight で拒否し、GraphicsPipelineExecutionHost の区間実行に残す。明示的な DependsOn は資源で表せない順序を追加するが、無関係な副作用を自動的に存続させない。既存の imported write、MarkOutput または KeepAlive で観測可能性を明示する。

### Graph の一回記録と完了までの所有権

基準の `src/Graphics/Lumyte.Graphics.RenderGraph/IRenderGraphLifetime.cs` は Validate/Dispose、`RenderGraph.cs` の Retain は compiled plan への所有者移管を持つ。`CompiledRenderGraph.cs` の Record/Dispose は一回記録と command 状態検査を持つが、submission receipt を受け取って実完了を通知する bridge はない。この追加では private friend bridge を Graphics.RenderGraph と Graphics.Pipelines の双方から新 adapter assembly に限定して公開し、公開 scope や global extension registry は増やさない。

Graphics.RenderGraph 側に追加する内部の transactional append は detached pass の検証、資源 identity 参照、use/lifetime の commit/rollback を一体で行う。Graphics.Pipelines 側の内部 bridge は plan の独立使用 lease と一回記録権を予約/commit/rollback し、record-only callback を発行する。adapter の IRenderGraphLifetime 実装が両者を結び、成功時に既存 graph.Retain と同じ lifetime 所有者リストへ保持する。これらの予約・transaction は新しい内部メンバーであり、現行 public AddPass/Retain だけで原子的 rollback ができるという主張ではない。friend-access はこの adapter assembly にだけ与え、GetOrAddExtension や登録表は使わない。

cull または未記録 abort は callback を実行せず、独立 lease は graph/compiled plan の Dispose まで保守的に保持してから返す。即時の pass 別 cull 通知は追加しない。record callback の実行だけを lease が一度記録し、その後は caller が GPU 実完了と command 破棄を保証して compiled graph を Dispose するまで返さない。既存 Dispose の Completed/Faulted/Disposed 判定だけを実完了証明とは扱わず、未送信 command 破棄と送信済み command の実完了後破棄を caller が区別する既存契約を維持する。adapter に submission receipt API や新たな完了通知は追加せず、既存 Retain による coarse な保持を再利用する。受付不明では単に command を破棄して安全になったとは扱わず、前節の隔離・信頼された世代退役証明を満たすまで graph/compiled plan と lease を保持する。cleanup 失敗は独立 lease を保持して再試行し、借用元 plan/pipeline の所有権は奪わない。

### 論理 pass adapter の公開 API 差分

以下は同じ基準ソースへの追加提案であり、attachment、Load/Store、Begin/End を管理する公開型は追加しない。

~~~diff
+namespace Lumyte.Graphics.Pipelines
+{
+    public sealed class PipelineResourceSlot
+    { internal PipelineResourceSlot(); } // plan manifest 内の opaque identity。
+    public sealed class GraphicsPipelineResourceManifest
+    {
+        public PipelineResourceSlot GetSlot(int index); // 捕捉済み順序。範囲外は拒否。
+        public int Count { get; }
+    }
+}
+namespace Lumyte.Graphics.Pipelines.RenderGraph
+{
+    public sealed record PipelineGraphTextureBinding(PipelineResourceSlot Slot,
+        RenderGraphTexture Resource, IGraphicsTexture Allocation,
+        TextureState State, BarrierScope Scope);
+    public sealed class PipelineGraphBufferBinding
+    {
+        public static PipelineGraphBufferBinding Create<T>(PipelineResourceSlot slot,
+            RenderGraphBuffer<T> resource, IGraphicsBuffer<T> allocation, BarrierScope scope)
+            where T : unmanaged; // 型と allocation/range は plan manifest に照合。
+    }
+    public sealed class PipelineGraphBindings
+    {
+        public PipelineGraphBindings(IReadOnlyList<PipelineGraphTextureBinding> textures,
+            IReadOnlyList<PipelineGraphBufferBinding> buffers); // null 拒否、不変コピー。
+    }
+    public static class PipelineGraphExtensions
+    {
+        // 全 preflight 後に原子的 append と一回記録権 claim。失敗は rollback。
+        public static RenderGraphPass AddPipelinePass(this RenderGraph graph, string name,
+            GraphicsPipelineRecordPlan plan, PipelineGraphBindings bindings);
+    }
+}
~~~

### 受付不明の隔離と信頼された退役証明

既存の queue/submission API には、例外となった Submit の受付を後から問い合わせる機能がない。したがって receipt がない受付不明区間に Drain の有限完了を保証しない。Drain は既知 submission の実完了と回収を試み、受付不明が残れば `PipelineQuarantinedException` を返して owner を host に残す。待ち続ければ安全になるとはしない。Retirement authority がない backend では、その隔離を解く機能はなく、host と依存 owner を保持し続ける必要がある。

退役に対応する backend は以下の新しい lifecycle service を実装する。これは現行 queue の機能を言い換えたものではない。Generation と Proof の発行は backend の信頼境界内だけに置き、公開 constructor や bool による自己申告は設けない。Graphics.Abstractions 内の非公開発行 factory と backend assembly の friend-access を追加し、Graphics.Pipelines には退役 proof を発行する能力を渡さない。authority なし用の local generation の発行だけは Graphics.Pipelines から利用可能とする。

~~~diff
+namespace Lumyte.Graphics.Abstractions
+{
+    public sealed class GraphicsDeviceGeneration
+    {
+        private GraphicsDeviceGeneration(); // device reference と発行 authority の非公開 identity。
+    }
+    public sealed class GraphicsDeviceRetirementProof
+    {
+        private GraphicsDeviceRetirementProof();
+        public GraphicsDeviceGeneration Generation { get; }
+    }
+    public interface IGraphicsDeviceRetirementAuthority
+    {
+        IGraphicDevice Device { get; }
+        GraphicsDeviceGeneration Generation { get; }
+        // backend が全 GPU 使用の終了と子資源の安全な回収を確認してからだけ返す。
+        ValueTask<GraphicsDeviceRetirementProof> WaitForRetirementAsync(
+            CancellationToken cancellationToken = default);
+    }
+}
+namespace Lumyte.Graphics.Pipelines
+{
+    public sealed record PipelineRecoveryState(GraphicsDeviceGeneration Generation,
+        int QuarantinedEpochs, int PendingCleanupOwners);
+    public sealed class PipelineQuarantinedException : InvalidOperationException
+    {
+        public PipelineRecoveryState RecoveryState { get; }
+    }
+}
~~~

private constructor を呼べる Graphics.Abstractions の発行サービスは、backend の device 初期化時だけ渡す非公開 issuer capability を検証する。Graphics.Pipelines に許す local generation の発行は proof 用 capability を返さない。backend 側の発行サービスはこの capability を持つ adapter であり、単に公開 interface を実装した任意の caller は新しい token/proof を作れない。build host に authority を渡す場合は Build 時に device を照合してその Generation を固定する。execution host は pipeline の実 device と token の発行者を constructor で検証し、Recover は constructor で固定した authority、device reference、generation identity が一致する proof だけを受ける。authority なしの build は Graphics.Abstractions 発行の local generation を固定し、execution host は pipeline の同じ token を使う。後から caller の証明でその token を置き換えることはできない。別世代・別 host に適合しない proof を拒否する。

Recover は送信済み計算が成功したとは扱わず、その世代の隔離資源を安全に回収可能へ移すだけである。CPU continuation と次の GPU 区間は再開しない。host は借用 device を Dispose せず、退役確認を求めるために device を強制破棄もしない。取得済み proof を使う Recover/Drain/Dispose の再試行で、回収に失敗した owner だけを保持する。主処理と cleanup の双方が失敗した場合は両例外を保持して報告し、回収成功を偽装しない。

owner context と authority は caller が host より長く保持する。authority の退役待機取消しは proof の発行や GPU の取消しを意味しない。backend に安全な退役確認がなければ authority を提供せず、未知受付の隔離保持という制約を明示する。

### 公開 API 差分

以下は基準コミットのソースに対する提案差分の抜粋である。Core の公開型は GPU 型を含まない。using、実装本体、値型の accessor は省略する。既存 constructor、命令別 interface、prepared 型は削除しない。IRenderer2D は外側の既存利用形を表す追加であり、汎用 pipeline の interface とは役割が異なる。

~~~diff
+namespace Lumyte.Pipelines
+{
+    public interface ISchema { static abstract SchemaDescriptor Descriptor { get; } }
+    public sealed record SchemaDescriptor(int Version, Type ElementType, int ElementStride,
+        IReadOnlyDictionary<string, object> Metadata); // 不変コピー。domain の解釈は拡張側。
+    public readonly struct Host<S> where S : ISchema;
+    public sealed class Port<V>
+    {
+        internal Port(); // graph identity 固定。native owner なし。
+        public Port<O> Then<O>(IStage<V, O> implementation);
+    }
+    public sealed class PipelineGraph<TInput>
+    {
+        public Port<TInput> Input { get; }
+        public Port<O> Stage<A, O>(IStage<A, O> implementation,
+            Port<A> a);
+        public Port<O> Stage<A, B, O>(IStage<A, B, O> implementation,
+            Port<A> a, Port<B> b);
+        public Port<O> Stage<A, B, C, O>(IStage<A, B, C, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c);
+        public Port<O> Stage<A, B, C, D, O>(IStage<A, B, C, D, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d);
+        public Port<O> Stage<A, B, C, D, E, O>(IStage<A, B, C, D, E, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d, Port<E> e);
+        public Port<O> Stage<A, B, C, D, E, F, O>(IStage<A, B, C, D, E, F, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d, Port<E> e, Port<F> f);
+        // 全設定・node/edge を不変捕捉して凍結。型・graph・循環・欠落を検証。
+        public PipelineDefinition<TInput, TOutput> Complete<TOutput>(Port<TOutput> output);
+    }
+    public sealed class PipelineDefinition<TInput, TOutput>
+    {
+        public PipelineDescription DescribeDataflow(); // 同じ定義。資源確保なし。
+    }
+    public interface IPipelineIdentity { } // 不透明 identity。native API を含まない。
+    public interface IPipeline<TInput, TOutput> : IDisposable, IPipelineIdentity
+    {
+        // submit なし。GPU 出力は記録計画の readiness であり実完了ではない。
+        ValueTask<PipelineResult<TOutput>> PrepareAsync(TInput input,
+            CancellationToken cancellationToken = default);
+    }
+    public sealed class PipelineResult<T> : IDisposable
+    {
+        public T Value { get; } // 出力の readiness を満たした時だけ。使用 lease は result が保持。
+        public void Dispose(); // 未完了は拒否し owner/retry を保持。
+    }
+    public sealed class PipelineBuildHost : IDisposable
+    {
+        public PipelineBuildHost(ExecutionRuntime execution, PipelineBudget budget);
+        public IPipeline<I, O> Build<I, O>(PipelineDefinition<I, O> definition);
+        public void Dispose(); // 未公開 owner と失敗 cleanup の再試行。
+    }
+    public interface IStage<A, O>
+    { IStageFactory<A, O> Factory { get; } }
+    public interface IStageFactory<A, O> : IStageFactory
+    { IStageProcessor<A, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, CancellationToken cancellationToken);
+    }
+    public interface IStage<A, B, O>
+    { IStageFactory<A, B, O> Factory { get; } }
+    public interface IStageFactory<A, B, O> : IStageFactory
+    { IStageProcessor<A, B, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, B, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, ValueLease<B> b, CancellationToken cancellationToken);
+    }
+    public interface IStage<A, B, C, O>
+    { IStageFactory<A, B, C, O> Factory { get; } }
+    public interface IStageFactory<A, B, C, O> : IStageFactory
+    { IStageProcessor<A, B, C, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, B, C, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, ValueLease<B> b, ValueLease<C> c, CancellationToken cancellationToken);
+    }
+    public interface IStage<A, B, C, D, O>
+    { IStageFactory<A, B, C, D, O> Factory { get; } }
+    public interface IStageFactory<A, B, C, D, O> : IStageFactory
+    { IStageProcessor<A, B, C, D, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, B, C, D, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, ValueLease<B> b, ValueLease<C> c, ValueLease<D> d, CancellationToken cancellationToken);
+    }
+    public interface IStage<A, B, C, D, E, O>
+    { IStageFactory<A, B, C, D, E, O> Factory { get; } }
+    public interface IStageFactory<A, B, C, D, E, O> : IStageFactory
+    { IStageProcessor<A, B, C, D, E, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, B, C, D, E, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, ValueLease<B> b, ValueLease<C> c, ValueLease<D> d, ValueLease<E> e, CancellationToken cancellationToken);
+    }
+    public interface IStage<A, B, C, D, E, F, O>
+    { IStageFactory<A, B, C, D, E, F, O> Factory { get; } }
+    public interface IStageFactory<A, B, C, D, E, F, O> : IStageFactory
+    { IStageProcessor<A, B, C, D, E, F, O> Create(StageBuildContext context); }
+    public interface IStageProcessor<A, B, C, D, E, F, O> : IDisposable
+    {
+        ValueTask<ValueLease<O>> PrepareAsync(StagePreparationContext context,
+            ValueLease<A> a, ValueLease<B> b, ValueLease<C> c, ValueLease<D> d, ValueLease<E> e, ValueLease<F> f, CancellationToken cancellationToken);
+    }
+    public interface IStageFactory
+    { StageRequirements Describe(StageInputDescriptions inputs); }
+    public sealed class ValueLease<V> { internal ValueLease(); }
+    public sealed class StageBuildContext
+    { public IStageCapabilities Capabilities { get; } } // build に明示供給された能力のみ。
+    public sealed class StagePreparationContext
+    {
+        public IStageCapabilities Capabilities { get; } // ambient/型 registry でなく当該 build の能力。
+        public V Read<V>(ValueLease<V> value); // CPU-readable のみ。
+        public ValueLease<V> Publish<V>(V immutableValue); // 検証・予算予約済み値のみ。
+        public ValueLease<V> Forward<V>(ValueLease<V> value); // 独立した使用 lease。
+        public SchemaData<S> ReadHost<S>(ValueLease<Host<S>> value) where S : ISchema;
+        public SchemaData<S> CopyHostData<S, T>(ReadOnlySpan<T> values, SchemaExtent extent)
+            where S : ISchema where T : unmanaged;
+        public ValueLease<Host<S>> PublishHost<S>(SchemaData<S> value) where S : ISchema;
+    }
+    public sealed record StageRequirements(long MaxHostBytes,
+        IReadOnlyList<IStageExtensionRequirement> Extensions);
+    public sealed record SchemaExtent(long ElementCount, IReadOnlyDictionary<string, object> Metadata);
+    public sealed class SchemaData<S> where S : ISchema
+    {
+        public SchemaExtent Extent { get; }
+        public ReadOnlySpan<T> ReadElements<T>() where T : unmanaged; // 規範 layout を検証。
+    }
+    public sealed class StageInputDescriptions
+    { public IReadOnlyList<SchemaExtent> Extents { get; } }
+    public static class Requirements
+    {
+        public static StageRequirements HostPassthrough<S>(long maxElements) where S : ISchema;
+    }
+    public interface IStageCapabilities { }
+    public interface IStageExtensionRequirement { } // 不変で有限の要求。実行能力ではない。
+}
+namespace Lumyte.Graphics.Pipelines
+{
+    public sealed record GraphicsPipelinePolicy(long MaxScratchBytes, long MaxTransferBytes,
+        long MaxReadbackBytes, int MaxEpochs); // 必須の有限非負値。epoch は正数。
+    public interface IGraphicsOwnerContext
+    { ValueTask InvokeAsync(Func<ValueTask> operation); } // native 操作と CPU 再開を直列化。
+    public interface IGraphicsStageCapabilities : IStageCapabilities
+    {
+        GraphicsStageBuildContext Build { get; }
+        GraphicsStagePreparationContext Preparation { get; }
+    }
+    public readonly struct Device<S> where S : ISchema;
+    public static class Transfers
+    {
+        public static Upload<S> Upload<S>() where S : ISchema;
+        public static Download<S> Download<S>() where S : ISchema;
+        public static DevicePassthrough<S> Passthrough<S>() where S : ISchema;
+    }
+    public sealed class Upload<S> : IStage<Host<S>, Device<S>> where S : ISchema;
+    public sealed class Download<S> : IStage<Device<S>, Host<S>> where S : ISchema;
+    public sealed class DevicePassthrough<S> : IStage<Device<S>, Device<S>> where S : ISchema;
+    public sealed class GraphicsStageRequirements : IStageExtensionRequirement
+    {
+        // 非負の有限 byte 上限、正当な domain/access/index を検証し、列を不変コピー。
+        // null、重複・競合する access、無効 key は ArgumentException。
+        public GraphicsStageRequirements(GraphicsExecutionDomain domain, long maxDeviceBytes,
+            IReadOnlyList<StageArtifactKey> artifacts, IReadOnlyList<StageInputAccess> inputAccesses);
+        public GraphicsExecutionDomain Domain { get; } // Compute/Raster/Mesh/明示転送。
+        public long MaxDeviceBytes { get; }
+        public IReadOnlyList<StageArtifactKey> Artifacts { get; }
+        public IReadOnlyList<StageInputAccess> InputAccesses { get; }
+    }
+    public sealed class GraphicsStageBuildContext
+    { public StageArtifactLease GetArtifact(StageArtifactKey key); } // 宣言済みのみ。
+    public sealed class GraphicsStagePreparationContext
+    {
+        public ValueLease<Device<S>> AllocateDeviceOutput<S>(SchemaExtent extent) where S : ISchema;
+        public DeviceBindings<S> GetDeviceBindings<S>(ValueLease<Device<S>> value) where S : ISchema;
+        public ValueLease<Device<S>> ForwardDevice<S>(ValueLease<Device<S>> value) where S : ISchema;
+        public void Encode<S>(ValueLease<Device<S>> output, Action<StageRecordContext> record)
+            where S : ISchema; // record-only。全入力と access を捕捉。
+    }
+    public sealed class StageRecordContext
+    { public IGraphicsCommandBuffer Commands { get; } } // 借用。Finish/Submit/破棄禁止。
+    public sealed class DeviceBindings<S> where S : ISchema
+    {
+        public BufferSlice<T> GetBuffer<T>(int bindingIndex) where T : unmanaged;
+        public IGraphicsTextureView GetTexture(int bindingIndex);
+    }
+    public sealed class GraphicsPipelineBuildHost : IDisposable
+    {
+        public GraphicsPipelineBuildHost(ExecutionRuntime execution, IGraphicDevice device,
+            IStageArtifactProvider artifacts, GraphicsPipelinePolicy policy,
+            IGraphicsDeviceRetirementAuthority? retirementAuthority = null);
+        public IPipeline<I, O> Build<I, O>(PipelineDefinition<I, O> definition);
+        public void Dispose(); // 失敗 cleanup を再試行。成功 pipeline は独立。
+    }
+    public static class GraphicsPipelineResultExtensions
+    {
+        // 同じ Graphics build と owner context に属す準備結果だけ。submit しない。
+        public static GraphicsPipelineRecordPlan AcquireRecordPlan<T>(this PipelineResult<T> result,
+            IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext);
+        public static GraphicsPipelineExecution AcquireExecution<T>(this PipelineResult<T> result,
+            IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext);
+    }
+    public sealed class GraphicsPipelineRecordPlan : IDisposable
+    {
+        public GraphicsPipelineResourceManifest Resources { get; } // 不変の全外部資源 manifest。
+        // no-readback のみ、一回。借用 command に記録するだけで Finish/Submit しない。
+        public void Record(IGraphicsCommandBuffer commands);
+        public void Dispose(); // 記録後は実完了・command 破棄まで owner を保持する。
+    }
+    public sealed class GraphicsPipelineExecution : IDisposable
+    {
+        internal GraphicsPipelineExecution(); // AcquireExecution または同じ内部 bridge だけが発行。
+        public void Dispose(); // host 受入れ後は完了/回収まで拒否し owner を保持。
+    }
+    public sealed class GraphicsPipelineExecutionHost : IDisposable
+    {
+        public GraphicsPipelineExecutionHost(IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext,
+            IGraphicsDeviceRetirementAuthority? retirementAuthority = null);
+        public ValueTask ExecuteAsync(GraphicsPipelineExecution execution, CancellationToken cancellationToken = default);
+        public ValueTask DrainAsync(); // 取消し不能。受付不明時は隔離したまま例外。
+        public PipelineRecoveryState RecoveryState { get; }
+        public ValueTask RecoverAsync(GraphicsDeviceRetirementProof proof);
+        public void Dispose(); // 未完了拒否。暗黙 wait なし。
+    }
+}
 namespace Lumyte.Graphics.Drawing2D
 {
+    public interface IRenderer2D : IDisposable
+    {
+        IGraphicDevice Device { get; }
+        DrawingRetentionStatistics2D RetentionStatistics { get; }
+        ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target,
+            CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
-    public sealed partial class Renderer2D : IDisposable
+    public sealed partial class Renderer2D : IRenderer2D, IAsyncPipelineRenderer2D
     {
+        // pipeline/device は借用。identity/device/schema/routes/policy 検証後に使用 lease を取得。
+        // resourceOwner の移管は成功時だけ。不一致は constructor 成功前に拒否。
+        public Renderer2D(IGraphicDevice device,
+            IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline,
+            CommandRoutes2D routes, PipelinePolicy2D policy,
+            DrawingResources2D? resources = null, IDisposable? resourceOwner = null,
+            Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null);
+        public IGraphicDevice Device { get; }
+        public ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+        // 通常 Prepare は readback pipeline の場合、確保前に拒否。既存 no-submit は維持。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed class RenderRequest2D; // 不変 context/commands/frame/resource の借用 snapshot。
+    public interface IAsyncPipelineRenderer2D : IRenderer2D
+    {
+        ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
+    public sealed class PreparedPipelineExecution2D : IDisposable
+    {
+        public GraphicsPipelineExecution Execution { get; } // 元 pipeline identity に固定。
+        public void Dispose(); // 未完了拒否。失敗 cleanup は再試行可能。
+    }
+}
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed class RenderGraphRenderer2D
     {
+        // 同じ直接借用 pipeline と source binding を借用。対応検査は Graph 変更前。
+        public RenderGraphRenderer2D(IRenderer2D renderer, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings);
     }
 }
~~~

GraphicsPipelineExecutionHost の identity 引数は compiled pipeline から得る読み取り専用 identity view であり、generic IPipeline はこの view を継承する。Core の view は不透明な identity だけで Graphics 型を含まず、host は Graphics build に由来しない identity を拒否する。RenderFields2D と具体 stage の主要な型は次の追加である。

~~~diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public static class RenderFields2D
+    {
+        public static IStage<RenderRequest2D, Host<CommandsV1>> Commands();
+        public static IStage<RenderRequest2D, Host<ResourceManifestV1>> ResourceManifest();
+        public static IStage<RenderRequest2D, Host<FrameV1>> Frame();
+        public static IStage<RenderRequest2D, Device<ResourceViewsV1>> ResourceViews();
+        public static IStage<RenderRequest2D, Device<TargetV1>> Target();
+    }
+    public sealed class CommandRoutes2D
+    {
+        // 11 種別を一度ずつ明示。値はこの graph に受入れ=true、事前拒否=false。
+        public CommandRoutes2D(bool fillRect, bool fillRoundedRect, bool fillCircle,
+            bool drawLine, bool fillPath, bool strokePath, bool drawImage,
+            bool drawCoverageMask, bool drawDistanceField, bool drawMesh, bool drawPaintGraph);
+    }
+    public sealed record SceneLimits2D(int MaxContours, int MaxSegments,
+        int MaxStrokeTriangles, int MaxTileReferences);
+    public sealed record SamplePolicy2D(int Count, int GridVersion); // Count は 16。
+    public sealed record PipelinePolicy2D(GraphicsPipelinePolicy Execution,
+        DrawingArgumentTableMode ArgumentTables, DrawingRetentionOptions2D Retention); // すべて必須。暗黙の予算なし。
+    public static class CpuStages2D
+    {
+        public static CpuNormalize2D Normalize(SceneLimits2D limits);
+        public static CpuValidateCoverage2D ValidateCoverage(SamplePolicy2D samples);
+    }
+    public sealed class CpuNormalize2D : IStage<Host<CommandsV1>,
+        Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>>;
+    public sealed class CpuValidateCoverage2D : IStage<Host<Coverage16V1>,
+        Host<FrameV1>, Host<Coverage16V1>>;
+    public static class ComputeStages2D
+    {
+        public static ComputeCoverage2D Coverage(SamplePolicy2D samples);
+        public static ComputePaint2D Paint(SamplePolicy2D samples);
+        public static ComputeClip2D Clip(SamplePolicy2D samples);
+    }
+    public sealed class ComputeCoverage2D : IStage<Device<SceneV1>,
+        Device<FrameV1>, Device<Coverage16V1>>;
+    public sealed class ComputePaint2D : IStage<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>;
+    public sealed class ComputeClip2D : IStage<Device<SceneV1>,
+        Device<FrameV1>, Device<Clip16V1>>;
+    public static class RasterStages2D
+    {
+        public static RasterCompose2D Compose(SamplePolicy2D samples);
+        public static RasterEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples);
+    }
+    public sealed class RasterCompose2D : IStage<Device<SceneV1>, Device<Coverage16V1>,
+        Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    public sealed class RasterEvaluateAndComposite2D : IStage<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    public static class MeshStages2D
+    {
+        public static MeshEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples);
+    }
+    public sealed class MeshEvaluateAndComposite2D : IStage<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    // 各 V1 型は ISchema と規範記述子を持つ独立した schema。
+}
~~~

### 外部 stage の構築と実処理

１〜６入力の IStage、factory、processor は上の Stage overload に一対一で対応し、組込みと外部実装は同じ境界を使う。factory は実装 descriptor が明示的に捕捉し、既知の具体型 switch や登録表は使わない。Describe は不変設定と入力 metadata から schema、capability、有限上限を返す。build は型と artifact inventory を、各準備は extent と展開上限を検査してから確保する。Create は検査後に node ごとに一度だけ遅延実行し、frame の可変状態は processor の一回分の lease に分離する。

Core context の拡張能力は build host が明示的に結合するもので、サービス探索ではない。Core の IStageCapabilities は空の境界であり、Graphics host の実体だけが IGraphicsStageCapabilities を実装する。processor は明示的な interface cast で build/準備 context を得て、不対応なら拒否する。型キーによる探索・登録はなく、Core は Graphics の型を参照しない。Graphics に結合する CPU stage は Host のみ、Compute/Raster/Mesh は Device のみを扱い、RenderFields2D の抽出は元の借用配置を保存する純粋な projection に限定する。Upload/Download は scheduler 所有の専用転送契約で、外部 factory が実行種別を偽って Device→Host を公開することはできない。

lease/binding constructor は非公開とし、別 node/準備、未確定 schema、失効世代を拒否する。CopyHostData は規範 layout、extent、有限予算を確保前に予約してコピーし、PublishHost はその不変データの所有権を接続する。Forward は別の使用 lease を作る。Graphics の AllocateDeviceOutput/Encode は宣言済み要求内だけを認め、Encode は全入力 lease と resource access を捕捉して後の区間へ記録 callback を登録するだけである。登録後の Device 出力を scheduler が依存計画として公開する。callback の Submit、Finish、wait、mapping、隠れた外部資源捕捉は禁止し、context に queue は渡さない。

外部 CPU 作者が readback 後の値を検査する具体例は次のとおりである。Requirements.HostPassthrough は入力と独立した出力使用 lease の有限予算も宣言する Core の helper とする。

~~~csharp
using Lumyte.Pipelines;
using Lumyte.Graphics.Drawing2D.Pipelines;

public sealed class FiniteCoverageCheck(long maxElements)
    : IStage<Host<Coverage16V1>, Host<Coverage16V1>>
{
    public IStageFactory<Host<Coverage16V1>, Host<Coverage16V1>> Factory { get; }
        = new FactoryImpl(maxElements);
    private sealed class FactoryImpl(long limit)
        : IStageFactory<Host<Coverage16V1>, Host<Coverage16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs)
            => Requirements.HostPassthrough<Coverage16V1>(limit);
        public IStageProcessor<Host<Coverage16V1>, Host<Coverage16V1>> Create(StageBuildContext context)
            => new Processor();
    }
    private sealed class Processor : IStageProcessor<Host<Coverage16V1>, Host<Coverage16V1>>
    {
        public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context,
            ValueLease<Host<Coverage16V1>> input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (float value in context.ReadHost(input).ReadElements<float>())
                if (!float.IsFinite(value) || value < 0 || value > 1)
                    throw new InvalidOperationException("Invalid coverage.");
            return ValueTask.FromResult(context.Forward(input));
        }
        public void Dispose() { }
    }
}
~~~

CPU の独立した新 payload も CopyHostData/PublishHost で出せるため、上例は pass-through 専用の閉じた API ではない。外部 GPU 作者も同じ factory/processor を実装し、GraphicsStageRequirements と artifact key を Describe し、Create の Graphics extension から宣言済み artifact を取得する。Prepare は Graphics extension で出力を確保し Encode に record-only dispatch/draw を渡す。内蔵型の判別を増やさず同じ Stage 呼出しに接続する。

### 完成済み shader と pass 資源の供給

基準コミットの `Execution/DrawingResources2D.cs` の `DrawingResources2D` は一つの graphics pipeline と任意 sampler の借用値であり、Compute や複数の新しい stage shader を供給する型ではない。新しい build host は別の不変 artifact provider を必須にする。通常例の `artifacts` は、各具体的な stage に対応した完成済み binary、反映済み ABI、pipeline/pass 定義を持ち、実行中の source compile や shader 融合をしない。

~~~diff
+namespace Lumyte.Graphics.Pipelines
+{
+    public sealed record StageArtifactKey(Type ImplementationType, int Version, int Index);
+    public interface IStageArtifactProvider
+    {
+        // GPU 確保なし。対応 backend target、schema/binding ABI、program hash を検証。
+        StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device);
+        StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context);
+    }
+    public sealed class StageArtifactBuildContext
+    {
+        public IGraphicDevice Device { get; }
+        public GraphicsDeviceGeneration Generation { get; } // Graphics host が device 世代へ固定。
+    }
+    public sealed record StageArtifactDescription(StageArtifactKey Key,
+        string BackendTarget, string ReflectedAbiHash, string ProgramHash);
+    public sealed class StageArtifactLease : IDisposable
+    {
+        public StageArtifactLease(StageArtifactDescription description,
+            IGraphicDevice device, GraphicsDeviceGeneration generation, IGraphicsPipeline? graphics,
+            IGraphicsComputePipeline? compute, IMeshPipeline? mesh, IGraphicsSampler? sampler,
+            IDisposable owner);
+        public StageArtifactDescription Description { get; }
+        public IGraphicsPipeline? Graphics { get; }
+        public IGraphicsComputePipeline? Compute { get; }
+        public IMeshPipeline? Mesh { get; }
+        public IGraphicsSampler? Sampler { get; }
+        public void Dispose(); // 使用 lease 返却後。失敗は scope が再試行。
+    }
+}
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed class BorrowedDrawingResourcesArtifactProvider2D : IStageArtifactProvider
+    {
+        // 明示した既存 ABI の key だけに借用 pipeline/sampler を供給する。
+        public BorrowedDrawingResourcesArtifactProvider2D(DrawingResources2D resources,
+            StageArtifactKey legacyKey);
+        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device);
+        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context);
+    }
+}
~~~

provider は immutable な artifact inventory を捕捉する。build context が渡す generation は同 device の lifecycle service と同じ token を使い、backend の資源世代変更時は新しい build を要求する。authority を提供しない場合は build ごとに local generation を固定するため、異なる build 間では artifact cache を共有しない。provider は Native 作成だけに device を使い、送信はしない。Describe を全 key に実行して capability、entry point、reflection の binding layout、schema version と program identity を照合した後だけ Acquire する。lease に含む pipeline の実 descriptor も照合し、provider の hash 宣言だけを信用しない。cache identity は artifact key、program/ABI、backend target、device reference と実資源 generation、供給 owner を含み、違う世代の pipeline を再利用しない。複数 shader/pass の依存 owner はその artifact lease が保持し、build scope が prepared 使用権の返却後に逆順回収する。

通常の新 graph は既存 `DrawingResources2D.Pipeline` を使わない。既存 pipeline を使いたい作者だけが借用 provider を明示し、その ABI と一致する stage へ接続する。新しい Compute/独立版/融合版の不足 artifact を既存 pipeline で埋めない。借用資源は caller または成功時に移管する resourceOwner が保持し、borrowed provider 自体は native owner を奪わない。既存 Renderer2D constructor の DrawingResources2D 引数は変更しない。

定義の `DescribeDataflow` は捕捉済みの同一の不変 node/edge、実装型、配置付き schema と明示転送だけを返す。後から build/Renderer2D に渡す予算や route は含まず、未束縛値を既定値で補わない。本 ADR では束縛後の統合 inspection API は追加せず、caller が明示した policy/routes を別に保持する。文字列の domain は診断表示だけで、配送や実装選択に使わない。実行前に実 pass 数、実入力の転送量や融合効果を推測して表示しない。

## 検討した代替案

- 論理 pass のために RenderPassScope/DrawContext を追加する: attachment や Load/Store、Begin/End の管理まで新たな外側へ広げてしまう。既存 AddPass と UseTexture/UseBuffer/DependsOn/KeepAlive を使い、汎用 record-plan adapter だけを追加する。

- 2D graph が外側の renderer を生成する: 描画だけなら短いが汎用再利用と直接注入を分離できない。汎用定義・compiled pipeline と Renderer2D 内部 binding を分離する。
- global registry で strategy を解決する: 引数は減るが選択と寿命が暗黙になる。constructor に instance を直接渡す。
- 全工程を fluent chain または文字列 edge にする: 分岐・合流または型検査が弱くなる。一入力は Then、分岐・合流は型付き変数と Stage にする。
- Resources/JobGraph をそのまま payload engine にする: 寿命と CPU scheduling は再利用できるが、typed payload と record-only GPU 出力は既存契約にない。typed lease lowering と Graphics host を追加する。
- 自動融合する: 資源予算と実行形態が不透明になる。ソースで完成済みの具体型を選ぶ。
- 同期 Record で readback を待つ: 未送信 command の結果は取得できない。送信を所有する非同期 host を明示する。

## 結果と影響

作者は全入力、schema、明示転送、具体実装、資源要求と有限予算を指定する。描画利用者は compiled IPipeline を Renderer2D に直接渡し、no-readback の外側 IRenderer2D と prepared owner を維持する。CPU 専用 pipeline は別 adapter から Graphics なしで使える。共通構文は全 backend/具体実装の利用可能性や性能優位を保証しない。

Browser Compute 構成は新境界の対象外として構築前に拒否する。基準コミットの `src/Graphics/Lumyte.Graphics.Browser/BrowserComputePipeline.cs` に汎用 Compute 型がないという意味ではない。他の device も必要な Compute/Mesh、storage format、readback がなければ確保前に拒否し、fallback しない。採用は実装済み・GPU 検証済みという意味ではない。

受入れ条件は Core の Graphics/2D 非依存、CPU 専用定義と別 adapter、任意 TInput/TOutput の Complete/build、compiled IPipeline の直接受渡しと外側 IRenderer2D の役割分離、request 全体の upload 禁止、Graphics passthrough、Stage/Then/転送 helper の同一性、凍結後追加・型不一致・graph 外 port・循環の拒否、fan-in/fan-out の一回評価である。schema/version/layout/sample/grid/source/世代の拒否、独立版と具体的融合版の数値一致、16 sample の平均後 SrcOver と部分重なり、mesh の center 補間、先取りのない painter order、有限予算の確保前拒否も確認する。

外部 assembly の１〜６入力 factory/processor と CPU/GPU 出力、artifact ABI/target/世代拒否、直接・retained・Graph の同一 pipeline/source/資源共有、borrowed owner と frame lease の解放、owner-context 上の区間再開、取消し・受付不明・mapping/cleanup 失敗と retry、ticket 二重使用拒否、偽造・別世代 proof 拒否、authority 不在時の隔離維持を検証する。論理 pass の全外部 manifest、実 identity/世代/alias、単一外部 texture 状態の preflight 拒否、transactional append 失敗 rollback と二重 claim 拒否、内部 scratch 予算、cull/未記録の coarse 保持、記録済み GPU 実完了・command 破棄までの独立 lease、no-readback と encoder 閉鎖、DependsOn と GPU 完了の区別、KeepAlive/出力の culling 規則も受入れ条件とする。文書 lint は実装・GPU 適合性検証を代替しない。
