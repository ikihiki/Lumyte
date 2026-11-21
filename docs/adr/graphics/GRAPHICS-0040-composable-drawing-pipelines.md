# ADR-GRAPHICS-0040: 直列チェーンと型付き分岐で描画パイプラインを構築する

- 状態: 採用
- 日付: 2026-11-17
- 更新日: 2026-11-21
- 基準コミット: `fcde70b3c84a620ab776ca19982ff678fe142315`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別 interface と DI](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)
- 変更対象: renderer 作者向けに、型付き DAG を一つの renderer へ構築する公開境界を追加する。
- 継続する判断: Canvas の記録、既存の命令別 interface、clip の数値規則、描画順、準備結果と資源所有権を維持する。

## 背景

renderer の作者は、CPU で形状を準備し、Compute で coverage を作り、Raster で合成する、といった処理を入力と出力で接続したい。現在の命令別 callback の交換だけでは、途中のデータ形式、転送、共有する計算、GPU 完了後に行う CPU 処理を構築式で表せない。

基準コミットの `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `Renderer2D.PrepareAsync` は、`DrawingCommandRenderers2D` の 11 種別へ配送し、一つの引数計画を確定する。同じ assembly の組込み経路は `Execution/Renderer2D.Retained.cs` の `TakeBuiltinBatch` と `Rendering/PreparedRecordBatch2D.cs` の `PrepareAsync` により種別をまたぐ連続命令をまとめ、fragment で形状、paint、clip を評価する。独立した coverage prepass や `ComputeStages2D` は現在の API ではない。

同じ基準の `Execution/PreparedDraw2D.cs` の `Record` は同期的に一度だけ記録する。`src/Graphics/Lumyte.Graphics.Abstractions/IGraphicsQueue.cs` の `Submit`、`IGraphicsSubmission.cs` の `WaitAsync`、`IGraphicsBuffer.cs` の `MapAsync` と `CopyTo` は別操作であり、現在の準備は送信しない。この境界のままでは GPU の出力を途中の CPU stage に渡せない。

## 決定

直列部分は `Then(implementation)` のチェーン、分岐と合流は `Stage(implementation, inputs...)` の型付き変数で記述する。両者を CPU・Compute・Raster・Mesh 共通の一つの DAG 構築へ統一し、意味上の schema と Host/Device 配置、明示的な転送を検証して `IRenderer2D` へ構築する。融合はソース内で完成した具体的な実装型の選択であり、チェーンの省略記法や実行時の自動最適化ではない。

### 利用者の変更前・変更後

変更前の利用者は固定配送表を渡す。次は基準コードの実在 API であり、`device`、`list`、`target`、`ct` は caller が用意する。

~~~csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, ct);
// prepared.Record(commands) → Finish → Submit → 実完了 → commands.Dispose。
// prepared はそれらの後で破棄する。
~~~

`artifacts` は後述の完成済み shader/pass 資源を供給する不変 provider で、caller が対象 backend 用に用意する。変更後の以下の型・factory は本 ADR の提案 API であり、基準実装に存在するという意味ではない。renderer 作者は空の graph に実装と全入力を接続する。通常の描画利用者は完成した `IRenderer2D` を受け取り、Canvas や準備後の直接描画手順を変えない。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

var sceneLimits = new SceneLimits2D(MaxContours: 2048, MaxSegments: 65536,
    MaxStrokeTriangles: 262144, MaxTileReferences: 1048576);
var samplePolicy = new SamplePolicy2D(Count: 16, GridVersion: 1);
var policy = new PipelinePolicy2D(MaxScratchBytes: 64L << 20,
    MaxTransferBytes: 128L << 20, MaxReadbackBytes: 64L << 20, MaxEpochs: 8,
    ArgumentTables: DrawingArgumentTableMode.Auto,
    Retention: new DrawingRetentionOptions2D {
        MaxFramesInFlight = 3, MaxCachedAssets = 512, MaxCachedBytes = 64L << 20 });
var routes = new CommandRoutes2D(fillRect: true, fillRoundedRect: true, fillCircle: true,
    drawLine: true, fillPath: true, strokePath: true, drawImage: true,
    drawCoverageMask: true, drawDistanceField: true, drawMesh: true, drawPaintGraph: true);
var g = new PipelineGraph2D();
PipelineInputs2D i = g.Inputs;
// 直列部分はチェーン。Then は直前の出力を次の一入力へ接続する。
var gpuScene = g
    .Stage(CpuStages2D.Normalize(sceneLimits),
        i.Commands, i.ResourceManifest, i.Frame)
    .Then(Transfers2D.Upload<SceneV1>());
var gpuFrame = g.Upload(i.Frame);

// 分岐は同じ型付き出力の再利用、合流は Stage の複数入力で表す。
var coverage = g.Stage(ComputeStages2D.Coverage(samplePolicy), gpuScene, gpuFrame);
var paint = g.Stage(ComputeStages2D.Paint(samplePolicy),
    gpuScene, i.ResourceViews, gpuFrame);
var clip = g.Stage(ComputeStages2D.Clip(samplePolicy), gpuScene, gpuFrame);
var output = g.Stage(RasterStages2D.Compose(samplePolicy),
    gpuScene, coverage, paint, clip, i.Target);
PipelineDeclaration2D declaration = g.Complete(output, routes, policy);

using var host = new RendererBuildHost2D(declaration, artifacts);
PipelineDescription2D description = host.DescribeDataflow();
using IRenderer2D renderer = host.BuildRenderer(device, resourceOwner, validateResources);
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, ct);
// 以後の記録・送信・完了確認・破棄は変更前と同じ。
~~~

上の数値は利用者が選ぶ有限予算の例であり、全入力を受け入れる保証ではない。`sceneLimits` は contour、展開後 segment、stroke triangle、tile reference の上限、`samplePolicy` は下記の 16 sample 規則である。`policy` は scratch・転送・readback・保持資源の予算、引数表方針、保持上限を必須値として渡す。`routes` はアルゴリズム preset ではなく、各種別をこの graph に受け入れるか拒否するかを宣言する固定表である。標準の初期化、fallback、欠落した stage の補完はない。

同じ入力からソースで完成した融合実装を使う場合は、上の coverage、paint、clip、compose の四つの node を次の一つに置き換える。他の構築・利用手順は同じである。

~~~csharp
var output = g.Stage(RasterStages2D.EvaluateAndComposite(samplePolicy),
    gpuScene, i.ResourceViews, gpuFrame, i.Target);
PipelineDeclaration2D declaration = g.Complete(output, routes, policy);
~~~

これは既製の融合 shader と pass を持つ具体型 `RasterEvaluateAndComposite2D` の選択である。独立版は本当に別 pass と中間保存を持ち、融合版は中間 coverage/paint/clip buffer を公開しない。独立版の ports を後から結合する optimizer、融合 hint、`FuseRequired`、source IR からの shader 生成は設けない。型を選んだ時点で物理的な処理の違いが確定する。

### 直列チェーン、分岐と合流の書き方

`g.Stage(stage, input)` と `input.Then(stage)` は、所有 graph に同形の一入力 node と辺を同じ規則で追加する。各呼出しは独立した node を追加し、両記法を呼んだからといって node identity を共有・重複排除しない。Then は入力を消費・再配置せず、新しい出力 port を返す。型の一致、所有 graph、凍結状態、null の検査も同じであり、チェーンの長さによって別 scheduler や追加 buffer を作らない。複数入力が必要な処理は長いチェーンへ押し込まず、途中結果を変数へ取り出して Stage で合流する。

`g.Upload(hostPort)` は `g.Stage(Transfers2D.Upload<S>(), hostPort)`、`g.Download(devicePort)` は Download stage の同形の省略記法である。S は port から推論する。チェーン中では `.Then(Transfers2D.Upload<S>())` と明示でき、どの記法でも転送 node を省略しない。構築時に CPU 処理・GPU 計算・転送・提出・完了待機は実行せず、実行時には同じ転送と同期が必要となる。

共有したい途中出力は一度変数へ束縛し、必要な node に渡す。`var next = previous.Then(stage)` を二回呼ぶと別 node が二つできるが、next を複数箇所で使っても producer は一つの Prepare で一回だけ評価する。GPU 実行の順序はソースの行順やメソッド呼出し順でなくデータ依存と target 書込み鎖から決める。

長い構成は、意味のある処理単位ごとに graph と型付き入力を受け、型付き出力を返す通常の C# helper へ分割できる。helper は同じ graph に明示した node を追加するだけで、別 renderer、既定アルゴリズム、隠れた転送、送信 loop を作らない。省略記法も外部 stage も同じ node/edge として DescribeDataflow に現れる。

### 型付き接続と schema

`Port<Value>` は graph 内の受動的な辺であり、実データ、完了待機、native owner を持たない。`Host<S>` と `Device<S>` は同じ意味 schema `S` の配置を表す。通常の CPU stage は Host の入出力、Compute・Raster・Mesh stage は Device の入出力を取り、配置を変えるのは明示的な Upload/Download node だけとする。CPU で作った別形式を Device の形式に見せる cast は許さず、意味や表現を変えるときも明示的な conversion stage を挿入する。

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

### 不変捕捉、共有構築と資源

`Complete` は node、実装設定、配線、routes、policy を不変にコピーする。宣言は device、実処理、scratch、Dispose を持たず、同じ宣言から別 host を作れる。実装 descriptor も受動的で不変とし、構築時に別の実処理を作る。設定の変更は新しい宣言と renderer の構築で行い、in-flight plan を差し替えない。

一つの Prepare は commands、frame、資源 generation を不変 snapshot として捕捉し、同じ node は全入力が揃った後に一度だけ評価する。別の node は同型・同設定でも別計算であり、共通部分式を自動抽出しない。この共有は Prepare 内だけで、別 frame の writable scratch、途中結果、未完了 submission を流用しない。

全 routes は同じ build scope、`DrawingResourceState2D` 相当の所有者 ledger と引数計画へ接続する。互換な不変 geometry と内部サービスは設定、実装 version、device、資源所有者・世代を含む意味 identity で構築内共有できる。型だけによる統合や global cache はしない。引数計画は Prepare ごとに一つを登録・確定し、各 stage が別 renderer を隠して構築する方式にはしない。

予算は展開後の geometry、tile/bin/scan、16 sample 中間、転送、readback、同時実行数の peak live bytes を含む。stroke、cap、join、miter、退化形状、clip 展開を含めて checked arithmetic で有限上限を導き、超過は確保前に拒否する。無制限の retry、暗黙の品質低下、精度不足の clamp はしない。宣言予算と backend capability、usage、format、owner context、転送・readback 対応を全体検査してから資源を確保する。実確保や device failure はエラーであり、別アルゴリズムへ fallback しない。

実行時の内部 lease は port と別物であり、通常の公開描画 API に stage runner を露出しない。`HostLease<S>` は CPU callback 中に使用可能な不変データ、`DeviceLease<S>` は資源世代と producer dependency を保持する。Device lease を取得しても GPU 完了を意味せず、scheduler だけが barrier と依存 submission を接続する。Download 後の Host lease は実完了と mapping/copy の成功後にだけ公開し、callback 終了後に保持するには別の所有 lease が必要となる。外部実装も owner を独断で破棄したり port を runtime handle として解決したりできない。

構築途中の失敗では未公開 owner を逆依存順に回収し、失敗分の retry を host に残す。`resourceOwner` は構築成功時だけ引き取り、失敗時は caller のままとする。構築済み renderer の寿命は host から独立し、prepared owner の全 lease が返るまで実処理・共有サービスを保持する。破棄に失敗した owner の再試行経路を失わない。

### CPU → GPU → CPU → GPU の実行

次の例は前例と同じ明示的な limits、samplePolicy、routes、policy と資源 binding を使う。`SceneV1` を upload し、Compute が作った `Coverage16V1` を readback して CPU で同 schema の規範範囲を検証して同内容を返し、再 upload して合成する例である。CPU の検証は各値が有限かつ規範範囲内であることを走査し、sample/grid/source identity と数値規則を維持する。不正値は拒否し、量子化版などへ変える場合は別 schema と conversion stage を必要とする。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

var g = new PipelineGraph2D();
var i = g.Inputs;
var ds = g
    .Stage(CpuStages2D.Normalize(sceneLimits), i.Commands, i.ResourceManifest, i.Frame)
    .Then(Transfers2D.Upload<SceneV1>());
var df = g.Upload(i.Frame);
var hc = g
    .Stage(ComputeStages2D.Coverage(samplePolicy), ds, df)
    .Then(Transfers2D.Download<Coverage16V1>());
// CPU 検証は frame との合流なので Stage へ戻す。
var validatedDevice = g
    .Stage(CpuStages2D.ValidateCoverage(samplePolicy), hc, i.Frame)
    .Then(Transfers2D.Upload<Coverage16V1>());
var paint = g.Stage(ComputeStages2D.Paint(samplePolicy), ds, i.ResourceViews, df);
var clip = g.Stage(ComputeStages2D.Clip(samplePolicy), ds, df);
var output = g.Stage(RasterStages2D.Compose(samplePolicy), ds, validatedDevice, paint, clip, i.Target);
using var build = new RendererBuildHost2D(g.Complete(output, routes, policy), artifacts);
using IRenderer2D renderer = build.BuildRenderer(device, resourceOwner, validateResources);
var segmented = (IAsyncPipelineRenderer2D)renderer;
using PreparedPipelineExecution2D execution = await segmented.PrepareExecutionAsync(list, target, ct);
// executionHost は caller が保持する長寿命の PipelineExecutionHost2D。
// 作成は new PipelineExecutionHost2D(segmented, ownerContext)。ownerContext も caller 提供。
try
{
    await executionHost.ExecuteAsync(execution, ct);
}
finally
{
    await executionHost.DrainAsync(); // cancellation 後も owner を失う前に回収。
}
// 成功時は最終 submission の実完了と command 回収まで済んでいる。
~~~

`PrepareExecutionAsync` は捕捉・検証と最初の CPU 区間までを行い、送信しない。readback を含む構築済み renderer は `IAsyncPipelineRenderer2D` も実装する。その graph の通常の `IRenderer2D.PrepareAsync` は、確保前に `NotSupportedException` とする。既存の同期 `PreparedDraw2D.Record` が CPU 再開や readback を隠れて実行することはない。readback のない graph は引き続き通常の高速経路を使える。

新しい host は次の区間実行を所有する。`ExecuteAsync` はこれをまとめる入口であり、通常の描画利用者に port の実行 loop を要求しない。

1. 準備された plan から次の GPU 区間を、host が作成・所有する command buffer に一度だけ Record する。readback 区間なら専用 Readback storage への copy までを記録する。
2. 記録時に plan identity、device generation、epoch、正確な command identity を対にした一回限りの ticket を発行する。host の Submit はその ticket だけを受け、Finish と queue Submit を行い、戻った submission を同じ epoch に登録する。任意の `IGraphicsSubmission` を caller が後付けする API は設けない。
3. 実完了を待ち、owner context へ戻って Readback allocation を MapAsync、CopyTo、Unmap する。mapping 自体が非同期の場合も完了を確認し、CPU 用所有配列への copy 後に初めて Host lease を公開する。CPU continuation を同じ owner context で評価してから次の upload と GPU 区間へ進む。
4. 最終区間の実完了後に command、mapping、scratch と依存 lease を逆順に回収する。成功した epoch を二重記録・二重送信・別 plan への登録に使えない。

host は送信受付と登録の間で cancellation を観測しない。Submit が例外となり受付が不明なら「未送信」に戻さず、実完了または確認済み device 世代退役まで command と全依存 owner を隔離する。確認済み未送信だけが command 破棄後に abort できる。待機 cancellation は GPU cancellation ではなく、host が進行と後始末の所有権を保持したまま caller に返す。取消し後の CPU continuation と追加送信は止めるが、受付済み区間と mapping request の完了は追跡する。

`ExecuteAsync` の取消し・失敗後も host と execution を同時に捨ててはならない。例の caller は `executionHost` を呼出し範囲外で保持し、finally の Drain 自体が失敗しても再試行する。`DrainAsync` が取消し不能の回収経路を提供し、cleanup 失敗は host が保持して次の Drain/Dispose で再試行する。受付不明の扱いと退役証明は次節の契約に従い、無条件の回収完了は保証しない。`Dispose` は暗黙に待機せず、未完了なら拒否して owner を保持する。backend の thread affinity は host の owner-context dispatcher で守り、単に await 後の任意 thread で mapping/record しない。

### 受付不明の隔離と信頼された退役証明

既存の queue/submission API には、例外となった Submit の受付を後から問い合わせる機能がない。したがって receipt がない受付不明区間に Drain の有限完了を保証しない。Drain は既知 submission の実完了と回収を試み、受付不明が残れば `PipelineQuarantinedException2D` を返して owner を host に残す。待ち続ければ安全になるとはしない。Retirement authority がない backend では、その隔離を解く機能はなく、host と依存 owner を保持し続ける必要がある。

退役に対応する backend は以下の新しい lifecycle service を実装する。これは現行 queue の機能を言い換えたものではない。Generation と Proof の発行は backend の信頼境界内だけに置き、公開 constructor や bool による自己申告は設けない。Graphics.Abstractions 内の非公開発行 factory と backend assembly の friend-access を追加し、Drawing2D には退役 proof を発行する能力を渡さない。authority なし用の local generation の発行だけは Drawing2D から利用可能とする。

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
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed record PipelineRecoveryState2D(GraphicsDeviceGeneration Generation,
+        int QuarantinedEpochs, int PendingCleanupOwners);
+    public sealed class PipelineQuarantinedException2D : InvalidOperationException
+    {
+        public PipelineRecoveryState2D RecoveryState { get; }
+    }
+}
~~~

private constructor を呼べる core の発行サービスは、backend の device 初期化時だけ渡す非公開 issuer capability を検証する。Drawing2D に許す local generation の発行は proof 用 capability を返さない。backend 側の発行サービスはこの capability を持つ adapter であり、単に公開 interface を実装した任意の caller は新しい token/proof を作れない。build host に authority を渡す場合は BuildRenderer 時に device を照合してその Generation を固定する。execution host は renderer の実 device と token の発行者を constructor で検証し、Recover は constructor で固定した authority、device reference、generation identity が一致する proof だけを受ける。authority なしの build は core 発行の local generation を固定し、execution host は renderer の同じ token を使う。後から caller の証明でその token を置き換えることはできない。別世代・別 host に適合しない proof を拒否する。

Recover は送信済み計算が成功したとは扱わず、その世代の隔離資源を安全に回収可能へ移すだけである。CPU continuation と次の GPU 区間は再開しない。host は借用 device を Dispose せず、退役確認を求めるために device を強制破棄もしない。取得済み proof を使う Recover/Drain/Dispose の再試行で、回収に失敗した owner だけを保持する。主処理と cleanup の双方が失敗した場合は両例外を保持して報告し、回収成功を偽装しない。

owner context と authority は caller が host より長く保持する。authority の退役待機取消しは proof の発行や GPU の取消しを意味しない。backend に安全な退役確認がなければ authority を提供せず、未知受付の隔離保持という制約を明示する。

### 配送、Canvas と RenderGraph

全 11 route は `FillRect`、`FillRoundedRect`、`FillCircle`、`DrawLine`、`FillPath`、`StrokePath`、`DrawImage`、`DrawCoverageMask`、`DrawDistanceField`、`DrawMesh`、`DrawPaintGraph` を明示する。表は命令受入れと graph 入力の抽出だけを行い、pass の順序や algorithm を決めない。対応不能を純粋な事前判定で拒否できるが、実行失敗後の候補切替は行わない。

Canvas は論理命令の記録だけを行い、stage や実行 backend を持たない。layer/effect と backdrop の意味は [GRAPHICS-0022 の決定](GRAPHICS-0022-2d-render-graph-layers.md#決定)を維持する。内部の RenderGraph は barrier、資源依存、transient allocation へ利用できるが、公開構築構文を RenderPass の列にしない。

通常経路の Graph 接続では Drawing2D assembly 内の friend bridge が同じ固定配送、資源 coordinator、引数方針と借用 lease を Drawing2D.RenderGraph assembly へ渡す。追加 constructor の `IRenderer2D` はこの bridge 対応を検査し、未対応の外部実装、異なる device、readback を含む graph を Graph の変更前に拒否する。内部 interface があるだけで外部実装すべてを接続可能とはしない。typed command、layer、backdrop、capture と失敗した Append の owner も同じ実借用を保持する。

直接・retained・Graph の完了通知と所有権を統合し、prepared owner を保持する。既存 retained frame の Record、Finish、Submit、AttachSubmission、実完了、command 破棄、Retire、Dispose の順序と、未送信 abort、取消し・受付不明時の隔離は変えない。元 renderer と借用 Graph host の準備は直列化する。cross-frame の資源保持・退役方針をこの DAG によって置き換えない。

### 公開 API 差分

以下は基準コミットのソースに対する追加・変更の抜粋である。型参照に必要な `Lumyte.Graphics.Abstractions` と `Lumyte.Graphics.Drawing2D` の using は差分から省略する。既存 constructor と命令別 interface は削除しない。namespace の省略を除き、例が使う接続・構築・実行メンバーをここにまとめる。実装本体は含まない。

~~~diff
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
+    public sealed partial class Renderer2D : IRenderer2D
     {
+        public IGraphicDevice Device { get; } // 借用。既存の Prepare は送信しない。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public interface ISchema2D
+    {
+        static abstract SchemaDescriptor2D Descriptor { get; }
+    }
+    public sealed record SchemaDescriptor2D(int Version, Type ElementType, int ElementStride,
+        int SampleCount, int GridVersion, string Coordinates, string ColorAlphaSemantics);
+    public readonly struct Host<S> where S : ISchema2D;
+    public readonly struct Device<S> where S : ISchema2D;
+    public sealed class Port<V>
+    {
+        internal Port(); // 所有 graph に固定。実データや実行能力は持たない。
+        // owner.Stage(implementation, this) と同じ node 追加。凍結後は拒否。
+        public Port<O> Then<O>(IStage2D<V, O> implementation);
+    }
+    public interface IStageDefinition2D { } // 不変設定と schema/capability 契約。
+    public interface IStage2D<A, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, O> Factory { get; }
+    }
+    public interface IStage2D<A, B, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, B, O> Factory { get; }
+    }
+    public interface IStage2D<A, B, C, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, B, C, O> Factory { get; }
+    }
+    public interface IStage2D<A, B, C, D, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, B, C, D, O> Factory { get; }
+    }
+    public interface IStage2D<A, B, C, D, E, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, B, C, D, E, O> Factory { get; }
+    }
+    public interface IStage2D<A, B, C, D, E, F, O> : IStageDefinition2D
+    {
+        IStageFactory2D<A, B, C, D, E, F, O> Factory { get; }
+    }
+    public sealed class PipelineGraph2D
+    {
+        public PipelineInputs2D Inputs { get; }
+        // 明示転送 node のみを追加。同 graph と配置・schema を検証。
+        public Port<Device<S>> Upload<S>(Port<Host<S>> input) where S : ISchema2D;
+        public Port<Host<S>> Download<S>(Port<Device<S>> input) where S : ISchema2D;
+        public Port<O> Stage<A, O>(IStage2D<A, O> implementation, Port<A> a);
+        public Port<O> Stage<A, B, O>(IStage2D<A, B, O> implementation, Port<A> a, Port<B> b);
+        public Port<O> Stage<A, B, C, O>(IStage2D<A, B, C, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c);
+        public Port<O> Stage<A, B, C, D, O>(IStage2D<A, B, C, D, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d);
+        public Port<O> Stage<A, B, C, D, E, O>(IStage2D<A, B, C, D, E, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d, Port<E> e);
+        public Port<O> Stage<A, B, C, D, E, F, O>(IStage2D<A, B, C, D, E, F, O> implementation,
+            Port<A> a, Port<B> b, Port<C> c, Port<D> d, Port<E> e, Port<F> f);
+        // 不変コピー。欠落 route、循環、graph 外 port、未順序の target 書込みを拒否。
+        public PipelineDeclaration2D Complete(Port<Device<TargetWritesV1>> output,
+            CommandRoutes2D routes, PipelinePolicy2D policy);
+    }
+    public sealed class PipelineInputs2D
+    {
+        public Port<Host<CommandsV1>> Commands { get; }
+        public Port<Host<ResourceManifestV1>> ResourceManifest { get; }
+        public Port<Device<ResourceViewsV1>> ResourceViews { get; }
+        public Port<Host<FrameV1>> Frame { get; }
+        public Port<Device<TargetV1>> Target { get; }
+    }
+    // 上記 schema 節の各 V1 型は独立した sealed ISchema2D marker と規範記述子を持つ。
+    public sealed class PipelineDeclaration2D; // 不変・受動的。GPU owner ではない。
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
+    public sealed record PipelinePolicy2D(long MaxScratchBytes, long MaxTransferBytes,
+        long MaxReadbackBytes, int MaxEpochs, DrawingArgumentTableMode ArgumentTables,
+        DrawingRetentionOptions2D Retention); // すべて必須。暗黙の予算なし。
+    public static class CpuStages2D
+    {
+        public static CpuNormalize2D Normalize(SceneLimits2D limits);
+        public static CpuValidateCoverage2D ValidateCoverage(SamplePolicy2D samples);
+    }
+    public sealed class CpuNormalize2D : IStage2D<Host<CommandsV1>,
+        Host<ResourceManifestV1>, Host<FrameV1>, Host<SceneV1>>;
+    public sealed class CpuValidateCoverage2D : IStage2D<Host<Coverage16V1>,
+        Host<FrameV1>, Host<Coverage16V1>>;
+    public static class Transfers2D
+    {
+        public static Upload2D<S> Upload<S>() where S : ISchema2D;
+        public static Download2D<S> Download<S>() where S : ISchema2D;
+    }
+    public sealed class Upload2D<S> : IStage2D<Host<S>, Device<S>> where S : ISchema2D;
+    public sealed class Download2D<S> : IStage2D<Device<S>, Host<S>> where S : ISchema2D;
+    public static class ComputeStages2D
+    {
+        public static ComputeCoverage2D Coverage(SamplePolicy2D samples);
+        public static ComputePaint2D Paint(SamplePolicy2D samples);
+        public static ComputeClip2D Clip(SamplePolicy2D samples);
+    }
+    public sealed class ComputeCoverage2D : IStage2D<Device<SceneV1>,
+        Device<FrameV1>, Device<Coverage16V1>>;
+    public sealed class ComputePaint2D : IStage2D<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>;
+    public sealed class ComputeClip2D : IStage2D<Device<SceneV1>,
+        Device<FrameV1>, Device<Clip16V1>>;
+    public static class RasterStages2D
+    {
+        public static RasterCompose2D Compose(SamplePolicy2D samples);
+        public static RasterEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples);
+    }
+    public sealed class RasterCompose2D : IStage2D<Device<SceneV1>, Device<Coverage16V1>,
+        Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    public sealed class RasterEvaluateAndComposite2D : IStage2D<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    public static class MeshStages2D
+    {
+        public static MeshEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples);
+    }
+    public sealed class MeshEvaluateAndComposite2D : IStage2D<Device<SceneV1>,
+        Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>;
+    public sealed class RendererBuildHost2D : IDisposable
+    {
+        public RendererBuildHost2D(PipelineDeclaration2D declaration, IStageArtifactProvider2D artifacts,
+            IGraphicsDeviceRetirementAuthority? retirementAuthority = null);
+        public PipelineDescription2D DescribeDataflow(); // 生成・確保・実行なし。
+        public IRenderer2D BuildRenderer(IGraphicDevice device, IDisposable? resourceOwner,
+            Action? validateResources);
+        public void Dispose(); // 未公開 owner の回収を再試行。
+    }
+    public sealed class PipelineDescription2D
+    {
+        public IReadOnlyList<StageDescription2D> Nodes { get; }
+        public IReadOnlyList<PortConnection2D> Edges { get; }
+        public CommandRoutes2D Routes { get; }
+        public PipelinePolicy2D Policy { get; }
+    }
+    public sealed record StageDescription2D(int NodeId, Type ImplementationType,
+        IReadOnlyList<Type> InputTypes, Type OutputType, string ExecutionDomain);
+    public sealed record PortConnection2D(int ProducerId, int ConsumerId, int InputIndex);
+    public interface IAsyncPipelineRenderer2D : IRenderer2D
+    {
+        GraphicsDeviceGeneration Generation { get; } // build に固定した token。
+        // 送信なし。通常 Prepare が表せない区間依存を持つ別 owner を返す。
+        ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(DrawList2D list,
+            RenderTarget2D target, CancellationToken cancellationToken = default);
+    }
+    public sealed class PreparedPipelineExecution2D : IDisposable
+    {
+        public void Dispose(); // 未完了は拒否。cleanup 失敗分は再試行可能。
+    }
+    public interface IPipelineOwnerContext2D
+    {
+        // 全 native 操作と CPU continuation の実行を owner context に直列化する。
+        ValueTask InvokeAsync(Func<ValueTask> operation);
+    }
+    public sealed class PipelineExecutionHost2D : IPipelineExecutionHost2D
+    {
+        // 借用 device/context。device 世代が違う execution と並行利用を拒否。
+        public PipelineExecutionHost2D(IAsyncPipelineRenderer2D renderer, IPipelineOwnerContext2D ownerContext,
+            IGraphicsDeviceRetirementAuthority? retirementAuthority = null);
+        public GraphicsDeviceGeneration Generation { get; }
+        public PipelineRecoveryState2D RecoveryState { get; }
+        public ValueTask RecoverAsync(GraphicsDeviceRetirementProof proof); // 正確な世代・発行者を検証。
+        public ValueTask ExecuteAsync(PreparedPipelineExecution2D execution,
+            CancellationToken cancellationToken = default);
+        public ValueTask DrainAsync();
+        public void Dispose(); // 未完了は拒否。回収失敗後も再試行できる。
+    }
+    public interface IPipelineExecutionHost2D : IDisposable
+    {
+        // 実装が command 作成、ticket 付き送信、owner-context 再開と実完了を所有。
+        ValueTask ExecuteAsync(PreparedPipelineExecution2D execution,
+            CancellationToken cancellationToken = default);
+        PipelineRecoveryState2D RecoveryState { get; }
+        ValueTask RecoverAsync(GraphicsDeviceRetirementProof proof);
+        ValueTask DrainAsync(); // 取消し不能。受付不明は隔離を維持し例外。有限完了を保証しない。
+        // Dispose は暗黙 wait なし。未完了なら拒否し owner を失わない。
+    }
+}
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed class RenderGraphRenderer2D
     {
+        // 対応 bridge、device、no-readback を graph 変更前に検証する。
+        public RenderGraphRenderer2D(IRenderer2D renderer, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings);
     }
 }
~~~

### 外部 stage の構築と実処理

`IStage2D` の factory は宣言が明示的に捕捉する。組込みと外部実装は同じ境界を使い、既知の具体型への switch や文字列 registry を使わない。factory の Describe は不変設定と入力 metadata から capability、schema と有限資源要求を返す。build では型と artifact inventory を検査し、各 Prepare で捕捉入力の extent と展開上限から全 node の Describe を検査してから資源を確保する。Create はその検査後に build 内の node ごとに一度だけ遅延実行し、以後も frame の確保前検査を省かない。処理 instance は node ごとに別であり、宣言・factory は可変の frame 状態を持たない。

以下の１〜６入力の factory/processor は上の Stage overload に一対一で対応する。

~~~diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public interface IStageFactory2D
+    {
+        StageRequirements2D Describe(StageInputDescriptions2D inputs);
+    }
+    public interface IStageProcessor2D : IDisposable { }
+    public interface IStageFactory2D<A, O> : IStageFactory2D
+    { IStageProcessor2D<A, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a,
+            CancellationToken cancellationToken);
+    }
+    public interface IStageFactory2D<A, B, O> : IStageFactory2D
+    { IStageProcessor2D<A, B, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, B, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a, StageValueLease2D<B> b,
+            CancellationToken cancellationToken);
+    }
+    public interface IStageFactory2D<A, B, C, O> : IStageFactory2D
+    { IStageProcessor2D<A, B, C, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, B, C, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a, StageValueLease2D<B> b, StageValueLease2D<C> c,
+            CancellationToken cancellationToken);
+    }
+    public interface IStageFactory2D<A, B, C, D, O> : IStageFactory2D
+    { IStageProcessor2D<A, B, C, D, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, B, C, D, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a, StageValueLease2D<B> b, StageValueLease2D<C> c, StageValueLease2D<D> d,
+            CancellationToken cancellationToken);
+    }
+    public interface IStageFactory2D<A, B, C, D, E, O> : IStageFactory2D
+    { IStageProcessor2D<A, B, C, D, E, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, B, C, D, E, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a, StageValueLease2D<B> b, StageValueLease2D<C> c, StageValueLease2D<D> d, StageValueLease2D<E> e,
+            CancellationToken cancellationToken);
+    }
+    public interface IStageFactory2D<A, B, C, D, E, F, O> : IStageFactory2D
+    { IStageProcessor2D<A, B, C, D, E, F, O> Create(StageBuildContext2D context); }
+    public interface IStageProcessor2D<A, B, C, D, E, F, O> : IStageProcessor2D
+    {
+        ValueTask<StageValueLease2D<O>> PrepareAsync(StagePreparationContext2D context,
+            StageValueLease2D<A> a, StageValueLease2D<B> b, StageValueLease2D<C> c, StageValueLease2D<D> d, StageValueLease2D<E> e, StageValueLease2D<F> f,
+            CancellationToken cancellationToken);
+    }
+    public sealed class StageValueLease2D<V> { internal StageValueLease2D(); }
+    public sealed class SchemaData2D<S> where S : ISchema2D
+    {
+        public SchemaExtent2D Extent { get; }
+        public ReadOnlySpan<T> ReadElements<T>() where T : unmanaged; // layout を検証。
+    }
+    public sealed record SchemaExtent2D(long ElementCount, int Width, int Height,
+        int SampleCount, int GridVersion, object SourceIdentity);
+    public sealed class StageInputDescriptions2D
+    {
+        public IReadOnlyList<SchemaExtent2D> Extents { get; } // 引数順。
+    }
+    public sealed class StageExecutionContract2D
+    {
+        public static StageExecutionContract2D Cpu { get; }
+        public static StageExecutionContract2D Compute { get; }
+        public static StageExecutionContract2D Raster { get; }
+        public static StageExecutionContract2D Mesh { get; }
+    }
+    public sealed record StageRequirements2D(StageExecutionContract2D Execution,
+        long MaxHostBytes, long MaxDeviceBytes, IReadOnlyList<StageArtifactKey2D> Artifacts,
+        IReadOnlyList<StageInputAccess2D> InputAccesses)
+    {
+        // 同 schema の CPU 検証。上限超過を確保前に拒否し、出力保持も計上。
+        public static StageRequirements2D CpuPassthrough<S>(long maxElements) where S : ISchema2D;
+    }
+    public sealed record StageInputAccess2D(int InputIndex, ResourceAccess Access);
+    public sealed class StageBuildContext2D
+    {
+        public StageArtifactLease2D GetArtifact(StageArtifactKey2D key); // 宣言済みだけ。
+    }
+    public sealed class StagePreparationContext2D
+    {
+        public SchemaData2D<S> ReadHost<S>(StageValueLease2D<Host<S>> value) where S : ISchema2D;
+        public SchemaData2D<S> CopyHostData<S, T>(ReadOnlySpan<T> values, SchemaExtent2D extent)
+            where S : ISchema2D where T : unmanaged; // 予算を予約しコピー。
+        public StageValueLease2D<Host<S>> ForwardHost<S>(StageValueLease2D<Host<S>> value)
+            where S : ISchema2D; // 出力の所有 lease を別に保持。
+        public StageValueLease2D<Host<S>> PublishHost<S>(SchemaData2D<S> value) where S : ISchema2D;
+        public StageValueLease2D<Device<S>> AllocateDeviceOutput<S>(SchemaExtent2D extent)
+            where S : ISchema2D; // 宣言予算内だけ。
+        public DeviceBindings2D<S> GetDeviceBindings<S>(StageValueLease2D<Device<S>> value)
+            where S : ISchema2D;
+        public void Encode<S>(StageValueLease2D<Device<S>> output,
+            Action<StageRecordContext2D> record) where S : ISchema2D;
+    }
+    public sealed class DeviceBindings2D<S> where S : ISchema2D
+    {
+        public BufferSlice<T> GetBuffer<T>(int bindingIndex) where T : unmanaged;
+        public IGraphicsTextureView GetTexture(int bindingIndex); // schema/ABI に照合済み。
+    }
+    public sealed class StageRecordContext2D
+    {
+        public IGraphicsCommandBuffer Commands { get; } // 借用。Finish/破棄禁止。
+    }
+}
~~~

CPU 処理は Host のみ、Compute/Raster/Mesh 処理は Device のみの型列を持つことを検査する。転送を許す Upload/Download の専用契約は scheduler が所有し、外部 factory が実行種別を偽って Device→Host を作ることはできない。schema 変更は別の typed stage とする。lease と binding の constructor は公開せず、別 node/Prepare の lease、未確定 schema、失効世代を拒否する。

PublishHost は同じ schema のコピー済み不変データに所有権を接続する。外部 CPU 作者が新しい payload を作る場合は、`context.CopyHostData<S, T>(ReadOnlySpan<T>, SchemaExtent2D)` によって規範 layout と上限を検証したデータを渡す。この操作も node の宣言予算を確保前に予約し、任意の pointer を借用する constructor はない。

Encode は当該 node の全入力 lease と宣言した資源 access を捕捉し、後の GPU 区間へ記録 callback を登録するだけである。CPU が device binding を読む、GPU 完了前に Host 出力を作る、callback が送信・待機・mapping・外部資源の隠れた捕捉を行うことは禁止する。出力は Encode 登録後にのみ公開でき、実行の完了状態は scheduler が付ける。context に queue は公開せず、command の Finish と所有権は host に残す。

例えば外部 assembly の作者は次の具体的な CPU stage を作れる。coverage の規範 layout は sample 順の float 配列であり、ReadElements はその layout と一致する場合だけ返す。

~~~csharp
using Lumyte.Graphics.Drawing2D.Pipelines;

public sealed class FiniteCoverageCheck(long maxElements)
    : IStage2D<Host<Coverage16V1>, Host<Coverage16V1>>
{
    public IStageFactory2D<Host<Coverage16V1>, Host<Coverage16V1>> Factory { get; }
        = new FactoryImpl(maxElements);

    private sealed class FactoryImpl(long limit)
        : IStageFactory2D<Host<Coverage16V1>, Host<Coverage16V1>>
    {
        public StageRequirements2D Describe(StageInputDescriptions2D inputs)
            => StageRequirements2D.CpuPassthrough<Coverage16V1>(limit);
        public IStageProcessor2D<Host<Coverage16V1>, Host<Coverage16V1>> Create(
            StageBuildContext2D context) => new Processor();
    }
    private sealed class Processor : IStageProcessor2D<Host<Coverage16V1>, Host<Coverage16V1>>
    {
        public ValueTask<StageValueLease2D<Host<Coverage16V1>>> PrepareAsync(
            StagePreparationContext2D context, StageValueLease2D<Host<Coverage16V1>> input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (float value in context.ReadHost(input).ReadElements<float>())
                if (!float.IsFinite(value) || value < 0 || value > 1)
                    throw new InvalidOperationException("Invalid coverage.");
            return ValueTask.FromResult(context.ForwardHost(input));
        }
        public void Dispose() { }
    }
}
~~~

readback 例の `validatedDevice` の定義を次に交換し、後続の合成は変えない。以下は同じ構築 scope 内の抜粋である。

~~~csharp
var validatedDevice = g
    .Stage(new FiniteCoverageCheck(16L * 1048576), hc)
    .Then(Transfers2D.Upload<Coverage16V1>());
~~~

### 完成済み shader と pass 資源の供給

基準コミットの `Execution/DrawingResources2D.cs` の `DrawingResources2D` は一つの graphics pipeline と任意 sampler の借用値であり、Compute や複数の新しい stage shader を供給する型ではない。新しい build host は別の不変 artifact provider を必須にする。通常例の `artifacts` は、各具体的な stage に対応した完成済み binary、反映済み ABI、pipeline/pass 定義を持ち、実行中の source compile や shader 融合をしない。

~~~diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed record StageArtifactKey2D(Type ImplementationType, int Version, int Index);
+    public interface IStageArtifactProvider2D
+    {
+        // GPU 確保なし。対応 backend target、schema/binding ABI、program hash を検証。
+        StageArtifactDescription2D Describe(StageArtifactKey2D key, IGraphicDevice device);
+        StageArtifactLease2D Acquire(StageArtifactKey2D key, StageArtifactBuildContext2D context);
+    }
+    public sealed class StageArtifactBuildContext2D
+    {
+        public IGraphicDevice Device { get; }
+        public GraphicsDeviceGeneration Generation { get; } // core が device 世代へ固定。
+    }
+    public sealed record StageArtifactDescription2D(StageArtifactKey2D Key,
+        string BackendTarget, string ReflectedAbiHash, string ProgramHash);
+    public sealed class StageArtifactLease2D : IDisposable
+    {
+        public StageArtifactLease2D(StageArtifactDescription2D description,
+            IGraphicDevice device, GraphicsDeviceGeneration generation, IGraphicsPipeline? graphics,
+            IGraphicsComputePipeline? compute, IMeshPipeline? mesh, IGraphicsSampler? sampler,
+            IDisposable owner);
+        public StageArtifactDescription2D Description { get; }
+        public IGraphicsPipeline? Graphics { get; }
+        public IGraphicsComputePipeline? Compute { get; }
+        public IMeshPipeline? Mesh { get; }
+        public IGraphicsSampler? Sampler { get; }
+        public void Dispose(); // 使用 lease 返却後。失敗は scope が再試行。
+    }
+    public sealed class BorrowedDrawingResourcesArtifactProvider2D : IStageArtifactProvider2D
+    {
+        // 明示した既存 ABI の key だけに借用 pipeline/sampler を供給する。
+        public BorrowedDrawingResourcesArtifactProvider2D(DrawingResources2D resources,
+            StageArtifactKey2D legacyKey);
+        public StageArtifactDescription2D Describe(StageArtifactKey2D key, IGraphicDevice device);
+        public StageArtifactLease2D Acquire(StageArtifactKey2D key, StageArtifactBuildContext2D context);
+    }
+}
~~~

provider は immutable な artifact inventory を捕捉する。build context が渡す generation は同 device の lifecycle service と同じ token を使い、backend の資源世代変更時は新しい build を要求する。authority を提供しない場合は build ごとに local generation を固定するため、異なる build 間では artifact cache を共有しない。provider は Native 作成だけに device を使い、送信はしない。Describe を全 key に実行して capability、entry point、reflection の binding layout、schema version と program identity を照合した後だけ Acquire する。lease に含む pipeline の実 descriptor も照合し、provider の hash 宣言だけを信用しない。cache identity は artifact key、program/ABI、backend target、device reference と実資源 generation、供給 owner を含み、違う世代の pipeline を再利用しない。複数 shader/pass の依存 owner はその artifact lease が保持し、build scope が prepared 使用権の返却後に逆順回収する。

通常の新 graph は既存 `DrawingResources2D.Pipeline` を使わない。既存 pipeline を使いたい作者だけが借用 provider を明示し、その ABI と一致する stage へ接続する。新しい Compute/独立版/融合版の不足 artifact を既存 pipeline で埋めない。借用資源は caller または成功時に移管する resourceOwner が保持し、borrowed provider 自体は native owner を奪わない。既存 Renderer2D constructor の DrawingResources2D 引数は変更しない。

`DescribeDataflow` は構築に使う同一の不変 node/edge から、実装型、配置付き schema、転送、route と予算を抽出する。文字列の domain は診断表示だけで、配送や実装選択に使わない。実行前に実 pass 数、実入力の転送量や融合効果を推測して表示しない。

## 検討した代替案

- 全工程を一つの fluent chain にする: 直列部分は短く読めるが、分岐・共有・複数入力を lambda や入れ子へ隠すことになる。Then は一入力の直列接続だけに用い、分岐・合流は型付き変数と Stage に戻す。
- 全工程を名前付き node と文字列 edge で別々に登録する: グラフ編集ツールと対応づけやすいが、手書きコードの改名・補完・接続型検査が弱くなる。診断名と port identity を分離し、接続は型付き値で表す。
- Composition の Slot/indexer を公開構文にする: 命令種別ごとの宣言には向くが、途中の型付き出力、fan-in/fan-out、明示転送を主語にできない。今回の公開境界は Stage と Port に統一する。
- RenderPass を公開構文にする: attachment と GPU 順序を直接表せるが、CPU 入出力を含む共通の意味 schema と異なる階層になる。内部の RenderGraph は再利用し、公開 grammar にはしない。
- 独立 stage を実行時に自動融合する: 同じ宣言から実行形態を変えられる一方、shader 生成・互換判定と資源予算が不透明になる。独立型とソースで完成した融合型を明示的に選ぶ。
- 命令種別ごとに完成 renderer を選ぶだけにする: 既存 callback の置換には十分だが、途中の計算と転送を組み立てられない。外側の固定配送は維持し、その内側を型付き graph にする。
- 同期 Record の内部で readback 待機する: 通常経路を変えずに見せられるが、未送信 command の結果は取得できない。送信責任を持つ別の非同期 host 境界を明示する。

## 結果と影響

renderer 作者は実装、全入力、転送、route、予算を明示する責務を負う。描画利用者は通常の `IRenderer2D` を使い、readback が必要な場合だけ非同期 execution host を選ぶ。CPU/Compute/Raster/Mesh を同じ構文で表せることは、各 backend と全 concrete 実装が利用可能であるという保証ではない。

この新しい構築境界では Browser backend の Compute 構成を対応対象にせず、構築前に拒否する。これは基準の `src/Graphics/Lumyte.Graphics.Browser/BrowserComputePipeline.cs` に汎用 Compute pipeline 型が存在しないという主張ではない。対象 device/backend で必要な Compute、Mesh、storage format、readback が未対応なら確保前に拒否し、Raster や CPU へ黙って切り替えない。本 ADR の採用は実装済み、GPU 検証済み、性能優位という意味ではない。

受入れ条件は、Stage と Then の node/edge・実行意味の同一性、Upload/Download 省略記法と明示転送 stage の同一性、凍結後のチェーン追加拒否、型不一致と graph 外 port の拒否、fan-in/fan-out と Prepare 内一回評価、schema/version/sample/grid/source/世代検証、外部 assembly の CPU と GPU stage の factory/processor 接続、artifact ABI/target/世代の拒否、独立版とソース完成融合版の数値一致、非二値 mesh の center 補間維持、16 sample の部分重なりと painter order、有限予算の確保前拒否である。直接・retained・Graph の共有構築と資源保持、受付不明・取消し・mapping 失敗・cleanup retry、owner-context 再開、ticket の二重使用拒否、偽造・別世代の退役証明拒否と authority 不在時の隔離維持も必要とする。document lint はこれらの実装・GPU 適合性検証を代替しない。
