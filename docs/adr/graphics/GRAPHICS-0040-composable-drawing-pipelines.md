# ADR-GRAPHICS-0040: パイプラインを構成して共通の IRenderer2D を作る

- 状態: 提案
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別配送](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)、[GRAPHICS-0033 の CPU 正規化](GRAPHICS-0033-drawing2d-normalized-clips.md#分類とスコープ)
- 変更対象: renderer の構築境界に型付きパイプラインと共通 interface を追加し、専用構成では内部記録形式と clip の評価場所を選べるようにする。
- 継続する判断: Canvas、命令別 renderer、標準構成の CPU clip 数値規則、描画順、既存の準備結果と資源所有権を維持する。

## 背景

renderer の作者は、矩形・パス・文字の個別実装だけでなく、CPU の形状処理、GPU coverage、全シーン compute などを組み合わせて renderer を作りたい。描画するアプリケーションには、その違いによらず Canvas と同じ準備・実行手順を使わせたいが、現在の固定配送だけでは方式ごとに異なる処理粒度を構成できない。

基準コミット `06c3f3d71aab42d4913d4634673fdc8e0162e495` の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs` の `PrepareAsync` は連続命令を `DrawingCommandRenderers2D` に配送する。`src/Graphics/Lumyte.Graphics.Drawing2D/Model/Canvas2D.cs` の `End()` は `DrawList2D` を返す。`src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.Retained.cs` は scene の read lease と snapshot を受け取る overload を持つ。この基準の `Renderer2D` は具体型であり、以下の共通 interface は本提案による追加である。

## 決定

renderer の作者が Lumyte.Composition の宣言と children indexer で入力、CPU 処理、packing、転送、GPU 処理、条件付き配送と合流を一つずつ構成し、最後に IRenderer2D を構築する。既定の方式・部品・設定値は設けず、同じグラフから処理一覧も生成する。通常の描画側は Canvas、PrepareAsync、既存の準備結果だけを使い続ける。

### 利用者の変更前・変更後

同じ矩形を描く例で比較する。変更前は基準コード、変更後と以降の宣言・構成例は提案である。device、target、rect、paint、cancellationToken と command・queue は呼出し側が用意し、target の状態遷移は省略する。

```csharp
using Lumyte.Graphics.Drawing2D;

var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(rect, paint);
DrawList2D list = canvas.End();
using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// prepared.Record(commands)、Finish、Submit、完了、commands.Dispose、prepared.Dispose。
```

変更後は renderer の構築箇所だけを交換する。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

// 例 1 の Composition 宣言 definition と明示設定を使う。
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
var canvas = new Canvas2D();
canvas.Begin();
canvas.FillRect(rect, paint);
DrawList2D list = canvas.End();
PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// 同じ Record・送信・完了後解放。通常の呼出しに Run/Plan/Bind を加えない。
```

definition の構築は後述の完全な例 1 に示す。通常の描画コードへ中間段階の実行や新しい frame owner を追加しない。

### 宣言、配送、所有権の契約

構築は空から始め、全 algorithm と設定を必須にする。既定 preset、null からの標準部品、エラー後の backend 切替えを設けない。CachePolicy.Disabled と OptimizationPolicy.PassThrough も明示する。既存の constructor と consumer の CancellationToken 省略は互換性として維持し、この制約の対象を新しい構築 API に限定する。

Composition は C# 宣言、factory、children indexer を生成する Core の仕組みであり、DI、実行 graph、GPU executor ではない。基準 SHA の `src/Core/Lumyte.Composition/ComposableAttribute.cs`、`src/Core/Lumyte.Composition/ComposeParameterAttribute.cs`、`src/Core/Lumyte.Composition/ComposeContentAttribute.cs`、`src/Core/Lumyte.Composition/ICompositionContent.cs` と `src/Core/Lumyte.Composition.Generators/CompositionGenerator.cs` の既存機構を使い、Drawing2D の host が宣言を型付き DAG に compile する。Core から Graphics への依存を作らず、新しい generator 機能を前提にしない。

- Sequence は子を順に処理する。FirstSupported は順序付き候補であり、並列実行ではない。凍結した論理描画または宣言された部分範囲を、最初に適格な候補へちょうど一回配送する。候補の入力集合は排他的に分割される。
- FirstSupported の predicate と admission は入力・feature snapshot の純粋な検査とする。Unsupported に限って次候補へ進め、全候補が非対応なら明示した Reject に従う。選択は GPU 書込み・記録・送信前に完了する。設定した容量 capability の不足も宣言上の非対応に限る。
- 計画中に私有確保が必要なら、公開前に完全 rollback できる場合だけ次候補へ進める。不正入力、schema、範囲外、資源失効、確保の実失敗、device failure、queue 受理不明、GPU error を Unsupported に変換しない。外向きには既存の例外契約で通知する。
- Fork は同じ読取り入力を両枝へ渡し、両方を論理的に実行する。Join は異なる役割の出力を照合する。GPU が並行実行できるのは資源が独立する場合だけで、同じ target への非可換な alpha 合成を siblings という理由で並列化しない。
- MergeOrdered は元の logical draw ordinal と expansion ordinal で結果を合流する。一描画から複数 triangle・stroke packet が出ても順序を保ち、途中の Path・Text を越えて矩形をまとめない。各枝は共通の OrderedRenderPackets2D を返し、別々の完成画像を後から混ぜて順序を再現したことにしない。

FillRect の kind だけでは適格性を決めない。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Model/FillRectCommand2D.cs` は Rect/Paint/State を記録する。変換・clip・paint を含む凍結状態が fast 経路の範囲外なら RectToPath が同じ塗り領域を表し、一般 path 経路へ送る。数値・paint・clip・参照寿命を引き継ぎ、塗りを輪郭に変えない。

点線の矩形輪郭を扱うには、入力に stroke/dash の論理意味が必要である。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Model/StrokeStyle2D.cs` は width/cap/join/miter を持ち dash を持たないため、本判断では FillRect から点線を推測せず、新しい Canvas 引数も追加しない。

互換入口は DrawList2D、scene read lease、snapshot の論理描画である。入口 adapter 自体も明示し、read lease は PrepareAsync の await 完了まで借用する。以後は独立所有または不変共有 lease を持つ。全シーン compute は per-shape hook を必要とせず、schema と演算粒度を作者が選べる。

### 公開 API 差分の正本

以下は基準 SHA に対する提案の宣言抜粋であり、実装やコンパイル済み API ではない。System、System.Collections.Generic、System.Threading、System.Threading.Tasks、Lumyte.Composition、Lumyte.Graphics.Abstractions を import する。記載した補助 contract は不変値で全フィールド必須とし、空値・未指定値を host が拒否する。

```diff
 namespace Lumyte.Graphics.Drawing2D
 {
+    public interface IRenderer2D : IDisposable
+    {
+        IGraphicDevice Device { get; } // 借用。renderer は破棄しない。
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
+        public IGraphicDevice Device { get; }
         // 既存の両 constructor、三つの PrepareAsync、RetentionStatistics、Dispose を維持。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public abstract class StageNode { /* 所有親と凍結状態を host が保持 */ }
+    public abstract class TypedStage<TIn, TOut> : StageNode
+    {
+        public Type InputType => typeof(TIn);
+        public Type OutputType => typeof(TOut);
+        public abstract TypedPorts<TIn, TOut> Ports { get; }
+    }
+    public abstract class ContainerStage<TIn, TOut> : TypedStage<TIn, TOut>
+    {
+        [ComposeContent] public OwnedContent Children { get; } = new();
+    }
+    public sealed class OwnedContent : ICompositionContent<StageNode>
+    {
+        // 配列をコピーし、null・重複・他親所有・再 Initialize を拒否する。
+        public void Initialize(StageNode[] content);
+    }
+    public interface IStageDefinition<TIn, TOut>
+    {
+        TypedPorts<TIn, TOut> Ports { get; }
+        StagePolicy Policy { get; }
+    }
+    public sealed record TypedSchema<T>(ReadOnlyMemory<byte> LayoutAndSemantics);
+    public sealed record TypedPorts<TIn, TOut>(TypedSchema<TIn> Input, TypedSchema<TOut> Output);
+    public sealed record StagePolicy(NumericContract Numeric, CachePolicy Cache,
+        OptimizationPolicy Optimization, long MaxWorkingBytes, AdmissionContract Admission);
+    public sealed record CpuStageDefinition<TIn, TOut>(TypedPorts<TIn, TOut> Ports,
+        Func<TIn, TOut> Transform, StagePolicy Policy) : IStageDefinition<TIn, TOut>;
+    public sealed record ResourceRange(int Slot, long Offset, long Length);
+    public sealed record KernelDefinition(ReadOnlyMemory<byte> Shader, string EntryPoint,
+        ReadOnlyMemory<byte> BindingAbi, ReadOnlyMemory<ResourceRange> Reads,
+        ReadOnlyMemory<ResourceRange> Writes, ulong RequiredCaps, long MaxWorkingBytes);
+    public sealed record GpuStageDefinition<TIn, TOut>(TypedPorts<TIn, TOut> Ports,
+        KernelDefinition Kernel, StagePolicy Policy, TargetContract Target)
+        : IStageDefinition<TIn, TOut>;
+    public sealed record TransferDefinition<TIn, TOut>(TypedPorts<TIn, TOut> Ports,
+        TransferContract Transfer, StagePolicy Policy) : IStageDefinition<TIn, TOut>;
+    public sealed record BindingPolicy(DrawingResources2D Resources, IDisposable ResourceLease,
+        Action ValidateResources, ArgumentTablePolicy ArgumentTables, SamplerContract Samplers);
+    public sealed record BuildOptions(long MaxWorkingBytes, int MaxFramesInFlight,
+        int MaxCachedAssets, long MaxCachedBytes, ExecutionPolicy Execution);
+    public sealed record SequenceConfig<TIn, TOut>(TypedPorts<TIn, TOut> Ports,
+        SequencePolicy Policy);
+    public sealed record ChoiceConfig<TIn, TOut>(TypedPorts<TIn, TOut> Ports,
+        SelectionPolicy SelectionPolicy, AllUnsupportedPolicy AllUnsupported);
+    public sealed record ForkConfig<TIn, TA, TB>(TypedSchema<TIn> Input,
+        TypedSchema<BranchPair<TA, TB>> Output, ForkPolicy Policy);
+    public sealed record JoinConfig<TA, TB, TOut>(TypedPorts<BranchPair<TA, TB>, TOut> Ports,
+        IStageDefinition<BranchPair<TA, TB>, TOut> Operation, JoinPolicy Policy);
+    public sealed record MergeConfig<T>(TypedPorts<T, T> Ports, OrderContract Order);
+    public sealed record RendererConfig(InputContract Input, TargetContract Target,
+        RendererPolicy Policy);
+    public sealed record SharedPort<T>(string LocalName, TypedSchema<T> Schema);
+    public sealed record ShareConfig<TIn, TOut>(SharedPort<TIn> Input,
+        SharedPort<TOut> Output, MergeConfig<TIn> IncomingOrder, SharePolicy Policy);
+    public sealed record BranchPair<TA, TB>(TA First, TB Second);
+    public sealed record ReferenceConfig<TIn, TOut>(SharedPort<TIn> Input,
+        SharedPort<TOut> Output, ReferencePolicy Policy);
+    public static partial class DrawingCompose
+    {
+        public static partial class Definitions
+        {
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Renderer : StageNode
+            {
+                [ComposeParameter] public required RendererConfig Config { get; init; }
+                [ComposeContent] public OwnedContent Children { get; } = new();
+                // 同じ compiled definition。寿命と凍結は後述。
+                public FlowDescription DescribeDataflow(IGraphicDevice device,
+                    BindingPolicy bindings, BuildOptions options);
+                public IRenderer2D BuildRenderer(IGraphicDevice device,
+                    BindingPolicy bindings, BuildOptions options);
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Stage<TIn, TOut> : TypedStage<TIn, TOut>
+            {
+                [ComposeParameter] public required IStageDefinition<TIn, TOut> Definition { get; init; }
+                public override TypedPorts<TIn, TOut> Ports => Definition.Ports;
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Sequence<TIn, TOut> : ContainerStage<TIn, TOut>
+            {
+                [ComposeParameter] public required SequenceConfig<TIn, TOut> Config { get; init; }
+                public override TypedPorts<TIn, TOut> Ports => Config.Ports;
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class FirstSupported<TIn, TOut> : ContainerStage<TIn, TOut>
+            {
+                [ComposeParameter] public required ChoiceConfig<TIn, TOut> Config { get; init; }
+                public override TypedPorts<TIn, TOut> Ports => Config.Ports;
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Fork<TIn, TA, TB> : ContainerStage<TIn, BranchPair<TA, TB>>
+            {
+                [ComposeParameter] public required ForkConfig<TIn, TA, TB> Config { get; init; }
+                public override TypedPorts<TIn, BranchPair<TA, TB>> Ports => new(Config.Input, Config.Output);
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Join<TA, TB, TOut> : TypedStage<BranchPair<TA, TB>, TOut>
+            {
+                [ComposeParameter] public required JoinConfig<TA, TB, TOut> Config { get; init; }
+                public override TypedPorts<BranchPair<TA, TB>, TOut> Ports => Config.Ports;
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class MergeOrdered<T> : TypedStage<T, T>
+            {
+                [ComposeParameter] public required MergeConfig<T> Config { get; init; }
+                public override TypedPorts<T, T> Ports => Config.Ports;
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Share<TIn, TOut> : ContainerStage<TIn, TOut>
+            {
+                [ComposeParameter] public required ShareConfig<TIn, TOut> Config { get; init; }
+                public override TypedPorts<TIn, TOut> Ports => new(Config.Input.Schema, Config.Output.Schema);
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Reference<TIn, TOut> : TypedStage<TIn, TOut>
+            {
+                [ComposeParameter] public required ReferenceConfig<TIn, TOut> Config { get; init; }
+                public override TypedPorts<TIn, TOut> Ports => new(Config.Input.Schema, Config.Output.Schema);
+            }
+        }
+    }
+}
```

全 component は public parameterless constructor を持てる class とし、algorithm を constructor で選ばない。required な ComposeParameter が factory の必須引数となる。OwnedContent は参照型であり boxing された struct を初期化しない。配列の clone と所有親の検証を一度に行い、失敗時は親所有を部分確定しない。空の収納 object は algorithm の既定値ではなく、未初期化 content は compile で拒否する。

生成される非 generic Renderer は factory delegate property、generic の Stage/Sequence 等はメソッドである。例えば Stage<TIn,TOut>(definition, with: null) と StageFactory<TIn,TOut>() が生成され、後者は delegate を返す。generic factory property、async Composable、追加の function adapter は仮定しない。generator の optional with は宣言操作の plumbing であり algorithm の既定値ではない。この host は with による未宣言 algorithm 追加や凍結後変更も拒否する。

異種 children は StageNode に格納されるため、全辺の型安全を C# コンパイルだけで証明しない。TypedStage は typeof と schema を保持し、host が Sequence の隣接 port、choice の共通入出力、fork/join の役割・資源、参照の入出力を GPU 確保前に照合する。閉じた generic 型を AOT 対象とし、高階型や可変個 generic 引数を必要としない。

### 共有 Path を一回の処理 node として使う

Share の子は定義を一箇所だけ所有する。Reference は graph 内だけで有効な SharedPort の型付き名前へ接続し、global registry や DI の型解決を行わない。定義の複製と一つの処理 node への複数入力を区別する。Share は宣言専用で root の逐次実行項目にはならず、全 Reference の入力集合を IncomingOrder で合流してから子を一回評価し、出力を要求元 ordinal へ対応付ける。Reference の入力収集と Share の一回評価は各 PrepareAsync 呼出しの評価 scope 内で完結し、別の呼出しや in-flight frame の入力・可変評価状態とは混在させない。

これにより FastRect 非対応の RectToPath と native Path が同じ PathSystem へ入る。OwnedContent の同じ子 instance を二親へ置くことは禁止する。複製するときは不変な部品定義を共有して新しい Stage 宣言を作り、処理を共有するときだけ Share/Reference を使う。参照は shader registry への opaque key ではない。

循環参照、dangling key、同名異 schema、必須参照の未到達、未生成値の使用、producer が consumer を支配しない依存、capability 不一致を compile で拒否する。複数入力は全て先に収集できる依存に限り、共有結果に依存して同じ共有入力を追加する循環は認めない。共有するのは完了済み不変 geometry とその lease であり、frame の書込み scratch・mask を共有しない。

### 構成例の明示入力

以下は全て提案で、同じ using を使う独立した構成である。s は schema 集合、a は単機能 CPU delegate、k は個別 shader provider の値、t は各転送契約であり、いずれも呼出し元が必須引数として渡す。不完全な値、null、設定欠落を拒否する。部品定義は algorithm ごとに一つで、部品集合から隠れた mode を選ばない。

```csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.Pipelines.Testing;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

var policy = new StagePolicy(numeric, CachePolicy.Disabled,
    OptimizationPolicy.PassThrough, 32L * 1024 * 1024, admissionContract);
var options = new BuildOptions(256L * 1024 * 1024, 3, 0, 0, executionPolicy);
var bindings = new BindingPolicy(resourceBindings, freshResourceLease, bindingValidation,
    argumentTablePolicy, samplerContract);
var rendererConfig = new RendererConfig(inputContract, targetContract, rendererPolicy);
var rawPolicy = policy with { Admission = rawAdmission };
var flattenPolicy = policy with { Admission = flattenAdmission };
var coveragePolicy = policy with { Admission = coverageAdmission };
var raw = new CpuStageDefinition<LogicalInput2D, RawCurves2D>(
    new(s.LogicalInput, s.RawCurves), a.EncodeRaw, rawPolicy);
var flatten = new CpuStageDefinition<RawCurves2D, CpuGeometry2D>(
    new(s.RawCurves, s.CpuGeometry), a.Flatten, flattenPolicy);
var coverage = new GpuStageDefinition<ResidentGeometry2D, Coverage2D>(
    new(s.ResidentGeometry, s.Coverage), k.Coverage, coveragePolicy, targetContract);
var pathInput = new SharedPort<PathBatch2D>("common-path-input", s.PathBatch);
var pathOutput = new SharedPort<OrderedRenderPackets2D>("common-path-output", s.OrderedPackets);
var pathShare = new ShareConfig<PathBatch2D, OrderedRenderPackets2D>(pathInput, pathOutput,
    new(new(s.PathBatch, s.PathBatch), painterOrder), sharePolicy);
var pathReference = new ReferenceConfig<PathBatch2D, OrderedRenderPackets2D>(
    pathInput, pathOutput, referencePolicy);
```

numeric は曲線許容差、predicate、丸め、16 サンプル格子、targetContract は format、色、premultiplied alpha、原点、DPR、extent、Load/Clear を全て指定する。executionPolicy は同時準備・有限 in-flight・完了方式、samplerContract は filter/address/LOD/比較、argumentTablePolicy は表方式、transfer 契約は用途・範囲・配置・整列を指定する。全例の bindings はそれぞれ新しい lease を持ち、一つの lease を複数 BuildRenderer へ二重移動しない。

以下の短い部品名も上の raw と同じ完全な定義値である。CPU は対応する入出力 schema、列挙した delegate、policy、GPU は schema、個別 kernel、policy、targetContract、転送は schema、個別 transfer 契約、policy を全て持つ。設定を補完する helper や複数 algorithm の preset ではない。

- capture: LogicalInput2D → DrawBatch2D。fastRect: DrawBatch2D → OrderedRenderPackets2D。nativePath: DrawBatch2D → PathBatch2D。rectToPath: DrawBatch2D → PathBatch2D。pathPackets: PathBatch2D → OrderedRenderPackets2D。全て CPU。各 capability は kind と凍結 state を検査する。
- packPackets: OrderedRenderPackets2D → PackedPackets2D、packGeometry: CpuGeometry2D → PackedGeometry2D、packStrips: SparseStrips2D → PackedStrips2D、packCurves: RawCurves2D → PackedCurves2D は CPU packing。
- uploadPackets/Geometry/Strips/Curves はそれぞれ packed → ResidentPackets2D/ResidentGeometry2D/ResidentStrips2D/ResidentCurves2D の明示転送。
- rasterCoverage: ResidentPackets2D → Coverage2D、coverage: ResidentGeometry2D → Coverage2D、paint: Coverage2D → SampleColor2D、resolve: SampleColor2D → ColorTarget2D は GPU。sparse: RawCurves2D → SparseStrips2D は CPU、stripPaint: ResidentStrips2D → SampleColor2D は GPU。
- expand: ResidentCurves2D → SceneGeometry2D、bin: SceneGeometry2D → SceneBins2D、scan: SceneBins2D → ScannedScene2D、compose: ScannedScene2D → SampleColor2D は個別 GPU algorithm。
- compact: LogicalInput2D → CompactScene2D、decode: CompactScene2D → RawCurves2D は個別 CPU 変換。mask: ResidentGeometry2D → Mask2D、paintSamples: ResidentGeometry2D → PaintSamples2D は独立 GPU 出力。

AdmissionContract は各部品に対する純粋な capability predicate、入力機能集合、容量上限、選択単位を必須で持つ。共通数値・資源設定を再利用しても AdmissionContract は部品専用であり、各短縮名の定義はその専用 policy を必須で受け取る。上の rawPolicy/flattenPolicy/coveragePolicy がその具体例である。fastRect は対応矩形だけ、rectToPath は矩形だけ、nativePath は path だけを受理する。Sequence の admission は全子の変換後 schema と capability を事前評価し、Reference の呼出し待ちより先に全入力を収集する。predicate が副作用を持つ定義は不正とする。

inputContract はここでは FillRect/FillPath、solid paint、明示 clip 範囲に限定する。他の rounded rect、circle、line、stroke、image、coverage/distance-field、text、mesh、paint graph を要求する宣言は対応部品なしには拒否する。全組込み対応とは呼ばず、文字を追加する場合も記録済み glyph を使い shaping を勝手に再実行しない。

各例の SequenceConfig は示す入出力 schema と明示 sequencePolicy を、ChoiceConfig は同じ schema、orderedFirstSupportedPolicy と AllUnsupportedPolicy.Reject を渡す。rendererPolicy、sequencePolicy、forkPolicy、joinPolicy、sharePolicy、referencePolicy、painterOrder は全必須項目を指定した値である。正数予算、設定された有限上限、選択単位、役割タグ、順序規則を欠く policy は拒否する。

SelectionPolicy は選択単位ごとの ordered-first-supported 規則を明示する不変値である。AdmissionContract の predicate は論理入力の凍結 view と capability snapshot だけを読み、StagePolicy を越える権限を要求できない。pathPackets は全 paint/clip/版情報を保持する packet encode だけを行い、後続 coverage/paint を内包しない。ForkPolicy は子 1 を mask、子 2 を paint samples と役割付け、JoinPolicy は logical/expansion ordinal とサンプル位置の一致を必須にする。

### 完全な Composition 構成例

例 1 は命令別配送を参考にした提案上の分解であり、基準実装の物理 pass と同一ではない。coverage buffer を使うこの分解を従来の direct shader と呼ばず、coverage・paint・resolve を暗黙融合しない。全例は入力から target まで列挙し、通常の利用者へ stage 実行を公開しない。

例 1: 条件付きの従来型。fast 矩形、矩形の path 化、native Path の順序付き候補。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, DrawBatch2D>(capture),
        FirstSupported<DrawBatch2D, OrderedRenderPackets2D>(
            new(new(s.DrawBatch, s.OrderedPackets), orderedFirstSupportedPolicy, AllUnsupportedPolicy.Reject))[
            Stage<DrawBatch2D, OrderedRenderPackets2D>(fastRect),
            Sequence<DrawBatch2D, OrderedRenderPackets2D>(new(new(s.DrawBatch, s.OrderedPackets), sequencePolicy))[
                Stage<DrawBatch2D, PathBatch2D>(rectToPath),
                Stage<PathBatch2D, OrderedRenderPackets2D>(pathPackets)
            ],
            Sequence<DrawBatch2D, OrderedRenderPackets2D>(new(new(s.DrawBatch, s.OrderedPackets), sequencePolicy))[
                Stage<DrawBatch2D, PathBatch2D>(nativePath),
                Stage<PathBatch2D, OrderedRenderPackets2D>(pathPackets)
            ]
        ],
        MergeOrdered<OrderedRenderPackets2D>(new(new(s.OrderedPackets, s.OrderedPackets), painterOrder)),
        Stage<OrderedRenderPackets2D, PackedPackets2D>(packPackets),
        Stage<PackedPackets2D, ResidentPackets2D>(uploadPackets),
        Stage<ResidentPackets2D, Coverage2D>(rasterCoverage),
        Stage<Coverage2D, SampleColor2D>(paint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 2: 共有 Path。fallback と native Path を同じ処理 node へ合流する。

```csharp
var definition = Renderer(rendererConfig)[
    Share<PathBatch2D, OrderedRenderPackets2D>(pathShare)[
        Stage<PathBatch2D, OrderedRenderPackets2D>(pathPackets)
    ],
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, DrawBatch2D>(capture),
        FirstSupported<DrawBatch2D, OrderedRenderPackets2D>(
            new(new(s.DrawBatch, s.OrderedPackets), orderedFirstSupportedPolicy, AllUnsupportedPolicy.Reject))[
            Stage<DrawBatch2D, OrderedRenderPackets2D>(fastRect),
            Sequence<DrawBatch2D, OrderedRenderPackets2D>(new(new(s.DrawBatch, s.OrderedPackets), sequencePolicy))[
                Stage<DrawBatch2D, PathBatch2D>(rectToPath),
                Reference<PathBatch2D, OrderedRenderPackets2D>(pathReference)
            ],
            Sequence<DrawBatch2D, OrderedRenderPackets2D>(new(new(s.DrawBatch, s.OrderedPackets), sequencePolicy))[
                Stage<DrawBatch2D, PathBatch2D>(nativePath),
                Reference<PathBatch2D, OrderedRenderPackets2D>(pathReference)
            ]
        ],
        MergeOrdered<OrderedRenderPackets2D>(new(new(s.OrderedPackets, s.OrderedPackets), painterOrder)),
        Stage<OrderedRenderPackets2D, PackedPackets2D>(packPackets),
        Stage<PackedPackets2D, ResidentPackets2D>(uploadPackets),
        Stage<ResidentPackets2D, Coverage2D>(rasterCoverage),
        Stage<Coverage2D, SampleColor2D>(paint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 3: 混成型。CPU geometry と GPU coverage・paint を明示する。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, RawCurves2D>(raw),
        Stage<RawCurves2D, CpuGeometry2D>(flatten),
        Stage<CpuGeometry2D, PackedGeometry2D>(packGeometry),
        Stage<PackedGeometry2D, ResidentGeometry2D>(uploadGeometry),
        Stage<ResidentGeometry2D, Coverage2D>(coverage),
        Stage<Coverage2D, SampleColor2D>(paint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 4: 疎な strip 型。CPU strip 化、packing、転送、GPU 順序付き paint。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, RawCurves2D>(raw),
        Stage<RawCurves2D, SparseStrips2D>(sparse),
        Stage<SparseStrips2D, PackedStrips2D>(packStrips),
        Stage<PackedStrips2D, ResidentStrips2D>(uploadStrips),
        Stage<ResidentStrips2D, SampleColor2D>(stripPaint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 5: 全シーン compute。raw、packing、転送、展開、bin、scan、compositor、resolve。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, RawCurves2D>(raw),
        Stage<RawCurves2D, PackedCurves2D>(packCurves),
        Stage<PackedCurves2D, ResidentCurves2D>(uploadCurves),
        Stage<ResidentCurves2D, SceneGeometry2D>(expand),
        Stage<SceneGeometry2D, SceneBins2D>(bin),
        Stage<SceneBins2D, ScannedScene2D>(scan),
        Stage<ScannedScene2D, SampleColor2D>(compose),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 6: 全 compute の独自 GPU binner。per-shape hook は不要。

customBin は次のように個別 provider の shader、ABI、範囲、capability、予算を明示して作る。

```csharp
var customBinKernel = new KernelDefinition(shaderProvider.BinShader, "bin_tiles",
    binAbi, binReads, binWrites, requiredBinCaps, 16L * 1024 * 1024);
var customBinPolicy = policy with { Admission = binAdmission };
var customBin = new GpuStageDefinition<SceneGeometry2D, SceneBins2D>(
    new(s.SceneGeometry, s.SceneBins), customBinKernel, customBinPolicy, targetContract);
```

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, RawCurves2D>(raw),
        Stage<RawCurves2D, PackedCurves2D>(packCurves),
        Stage<PackedCurves2D, ResidentCurves2D>(uploadCurves),
        Stage<ResidentCurves2D, SceneGeometry2D>(expand),
        Stage<SceneGeometry2D, SceneBins2D>(customBin),
        Stage<SceneBins2D, ScannedScene2D>(scan),
        Stage<ScannedScene2D, SampleColor2D>(compose),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 7: 内部 path 表現の変換。compact 記録を raw 曲線へ明示 decode。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, CompactScene2D>(compact),
        Stage<CompactScene2D, RawCurves2D>(decode),
        Stage<RawCurves2D, SparseStrips2D>(sparse),
        Stage<SparseStrips2D, PackedStrips2D>(packStrips),
        Stage<PackedStrips2D, ResidentStrips2D>(uploadStrips),
        Stage<ResidentStrips2D, SampleColor2D>(stripPaint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 8: CPU fixture。入力 manifest 検証、geometry 読取り、packing と転送。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, ValidatedInput2D>(validateFixture),
        Stage<ValidatedInput2D, CpuGeometry2D>(readCpuFixture),
        Stage<CpuGeometry2D, PackedGeometry2D>(packGeometry),
        Stage<PackedGeometry2D, ResidentGeometry2D>(uploadGeometry),
        Stage<ResidentGeometry2D, Coverage2D>(coverage),
        Stage<Coverage2D, SampleColor2D>(paint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 9: GPU fixture。完了済み resident geometry の読取り import。

producer は consumer とは別 owner のテスト支援 scope で実転送・送信・正常完了・command 破棄・退役を済ませる。

```csharp
using ResidentGeometryFixture resident = await FixtureProducer.UploadAndCompleteAsync(
    device, packedFixture, expectedInput, uploadGeometry, producerOptions, cancellationToken);
var importResident = FixtureStages.ImportResident(resident, expectedInput,
    new(s.ValidatedInput, s.ResidentGeometry), policy);
```

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, ValidatedInput2D>(validateFixture),
        Stage<ValidatedInput2D, ResidentGeometry2D>(importResident),
        Stage<ResidentGeometry2D, Coverage2D>(coverage),
        Stage<Coverage2D, SampleColor2D>(paint),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

例 10: Fork と Join。mask と paint samples を両方生成して相関合流。

```csharp
var definition = Renderer(rendererConfig)[
    Sequence<LogicalInput2D, ColorTarget2D>(new(new(s.LogicalInput, s.ColorTarget), sequencePolicy))[
        Stage<LogicalInput2D, RawCurves2D>(raw),
        Stage<RawCurves2D, CpuGeometry2D>(flatten),
        Stage<CpuGeometry2D, PackedGeometry2D>(packGeometry),
        Stage<PackedGeometry2D, ResidentGeometry2D>(uploadGeometry),
        Fork<ResidentGeometry2D, Mask2D, PaintSamples2D>(
            new(s.ResidentGeometry, s.MaskPaintPair, forkPolicy))[
            Stage<ResidentGeometry2D, Mask2D>(mask),
            Stage<ResidentGeometry2D, PaintSamples2D>(paintSamples)
        ],
        Join<Mask2D, PaintSamples2D, SampleColor2D>(
            new(new(s.MaskPaintPair, s.SampleColor), maskedJoin, joinPolicy)),
        Stage<SampleColor2D, ColorTarget2D>(resolve)
    ]
];
using IRenderer2D renderer = definition.BuildRenderer(device, bindings, options);
```

### 同じ compiled definition の処理一覧と検証

最初の DescribeDataflow または BuildRenderer で宣言 tree・全参照・feature snapshot・構築設定を凍結する。後からの children 初期化・設定変更を拒否し、異なる device 世代・policy で再利用しない。両操作は同じ compiled definition を使い、inspection は binding lease を移動せず、成功した BuildRenderer だけが移動する。定義は shader・ABI・schema・range の可変配列を clone し、借用 delegate は呼出し中だけ入力を借り独立所有出力を返す。

FlowDescription は不変の node ID、具体的 algorithm、CPU/GPU/transfer phase、typeof と schema を持つ port、依存辺、明示 policy/cache、資源用途・範囲、choice の候補順位・選択単位、Reference の接続番号を返す。algorithm と導出した allocation/barrier/scheduling は別分類にする。label は診断専用で検索 key ではなく、手書きの対応表や別の図を正本にしない。

```diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public sealed record FlowNode(int Id, string ConcreteStage, string Phase,
+        ReadOnlyMemory<PortDescription> Ports, ReadOnlyMemory<int> Dependencies,
+        ReadOnlyMemory<PolicyDescription> Policies, ReadOnlyMemory<ResourceUse> Resources,
+        ReadOnlyMemory<ChoiceRoute> Choices, ReadOnlyMemory<ReferenceRoute> References);
+    public sealed record FlowDescription(ReadOnlyMemory<FlowNode> Nodes,
+        ReadOnlyMemory<DerivedMechanic> DerivedMechanics);
+}
```

```csharp
FlowDescription flow = definition.DescribeDataflow(device, bindings, options);
foreach (FlowNode node in flow.Nodes.Span)
    Console.WriteLine($"{node.Id}: {node.Phase} {node.ConcreteStage}");
// 同じ node の Ports/Dependencies/Policies/Resources/Choices/References も表示できる。
```

例 2 の compiled graph は fast 矩形の packet と、RectToPath/nativePath から番号順に集めた共通 PathSystem の packet を最終 MergeOrdered で合わせる。FirstSupported の候補を GPU 並列枝のように表示しない。例 10 の Fork は両枝が必要な依存として表示する。cache hit や入力ごとの候補選択は実行範囲の差であり、全静的 node を毎回実行したとは表示しない。内部 observer の実 pass と照合できるが、新しい公開ログ framework や consumer の trace API は設けない。

host は循環、未接続 port、未処理 route、孤立した必須出力、複数 writer/hazard、役割タグ・格子・原点・DPR・extent・色・alpha・Load/Clear、schema・shader ABI・capability・予算の不一致を GPU 確保前に拒否する。最終出力は ColorTarget2D の書込み計画であり、利用者へ渡す新画像 owner ではない。外部 recording protocol の暗黙 adapter も挿入しない。

入力依存の count/offset、device 世代、累積展開量は PrepareAsync でも checked 検証する。stroke の width/cap/join/miter、縮退 contour、暗黙閉辺、tile 参照、scan/backdrop/scratch を含む有限上限を求め、精度条件以下へ切り詰めない。pool/storage の alignment、read/write scope、外部参照の到達可能性、引数表の推移的所有者まで検証する。barrier と allocation は明示 access から導出するが、upload・bin・scan・coverage の暗黙挿入や自動融合は行わない。

### 固定入力の検証契約

fixture の入口も単独段階として提供し、packing、upload、coverage を内包させない。以下も提案の追加 API 抜粋であり、通常の描画側には露出しない。

```diff
+namespace Lumyte.Graphics.Drawing2D.Pipelines.Testing
+{
+    public sealed class FixtureInputManifest
+    {
+        public static FixtureInputManifest Capture(DrawingSnapshot2D source,
+            RenderTarget2D target, ManifestContract contract);
+    }
+    public sealed class ResidentGeometryFixture : IDisposable { public void Dispose(); }
+    public static class FixtureStages
+    {
+        public static IStageDefinition<LogicalInput2D, ValidatedInput2D> ValidateInput(
+            FixtureInputManifest expected, TypedPorts<LogicalInput2D, ValidatedInput2D> ports, StagePolicy policy);
+        public static IStageDefinition<ValidatedInput2D, CpuGeometry2D> ReadCpu(CpuGeometry2D fixture,
+            FixtureInputManifest expected, TypedPorts<ValidatedInput2D, CpuGeometry2D> ports, StagePolicy policy);
+        public static IStageDefinition<ValidatedInput2D, ResidentGeometry2D> ImportResident(
+            ResidentGeometryFixture fixture, FixtureInputManifest expected,
+            TypedPorts<ValidatedInput2D, ResidentGeometry2D> ports,
+            StagePolicy policy);
+    }
+    public static class FixtureProducer
+    {
+        // テスト支援だけが Submit/Wait する。producer は consumer と別 scope。
+        public static ValueTask<ResidentGeometryFixture> UploadAndCompleteAsync(
+            IGraphicDevice device, PackedGeometry2D packed, FixtureInputManifest expected,
+            IStageDefinition<PackedGeometry2D, ResidentGeometry2D> upload, ProducerOptions options,
+            CancellationToken cancellationToken);
+    }
+}
```

validateFixture は ValidateInput(expectedInput, new(s.LogicalInput, s.ValidatedInput), policy)、readCpuFixture は ReadCpu(geometryFixture, expectedInput, new(s.ValidatedInput, s.CpuGeometry), policy) で作る。全三つの PrepareAsync の実入力・clip・paint・glyph/画像参照版・描画順・target・格子を manifest と照合し、不一致は GPU 操作前に拒否する。fixture を返すだけで caller 入力を無視する renderer にしない。

manifest は形式版、schema layout・stride・numeric 契約、count・offset と依存版、DPR・原点・extent・格子・色を持つ。不信な offset は checked 演算で範囲検証する。checksum は照合補助で、所有権・GPU 初期化の証拠にはしない。保存は既存 DTO/serializer を使え、記号資源 ID のみを持ち native handle や device は直列化せず再束縛時に検証する。

CPU fixture は構築時に独立所有または不変共有 lease を取得する。GPU fixture は成功した producer の実記録、Submit、正常完了、command 破棄、producer 退役を確認してから有効になる。import は device と世代を検証して読取り lease を取得するだけで coverage を行わない。構築済み renderer は元 handle から独立した lease を持ち、consumer の mask・scratch・出力を別確保する。producerOptions は別 owner、MaxWorkingBytes、MaxFramesInFlight、MaxCachedAssets、MaxCachedBytes、明示 execution・transfer 契約を全て必須とし、consumer の owner を二重移動しない。

不正設定は ArgumentException、機能・予算超過は NotSupportedException、期限切れ所有権は InvalidOperationException とする。native 確保後の失敗では取得済み私有資源を回収し、成功時だけ binding の所有権を移動する。

構築済み renderer は Drawing2D assembly 内で入力変換・計画・owner・実行 bridge を所有し、既存 PreparedDraw2D/PreparedDrawingFrame2D の内部 batch を拡張して compute を記録する。通常の戻り値から実行 context・汎用 runner・新 frame 型へ到達させない。

readback 支援は出力 allocation owner を最後の独立読取り lease まで保持し、実行資源の退役と分離する。oracle は対象実装の出力コピーでなく独立に用意する。CPU 準備・転送・送信・待機を計時外へ置いた比較を全体の改善とは呼ばない。

CPU ジョブ配送には基準 SHA の `src/Core/Lumyte.Execution/CommandPort.cs`、`src/Core/Lumyte.Execution/WorkflowPort.cs`、`src/Core/Lumyte.Execution/ModuleBuilder.cs` の既存 port・workflow、資源ロードには `src/Core/Lumyte.Resources/IResourceTransform.Typed.cs` の transform を使える。別 scheduler やロード完了 token を GPU 完了の証拠にしない。

### 通常の実行と寿命

全構成共通: どの構成でも retained 描画の実行は同じである。drawingSnapshot は caller が await 完了まで保持する既存 snapshot、commands は記録中、queue は同じ device のものとする。scene read lease も同じ PrepareAsync 手順を使う。

```csharp
PreparedDrawingFrame2D frame = await renderer.PrepareAsync(
    drawingSnapshot, target, cancellationToken);
frame.Record(commands);
commands.Finish();
var actual = queue.Submit([commands]);
frame.AttachSubmission(actual);
await actual.WaitAsync(CancellationToken.None);
commands.Dispose();
frame.Retire();
frame.Dispose();
actual.Dispose();
```

基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDraw2D.cs` と `src/Graphics/Lumyte.Graphics.Drawing2D/Execution/PreparedDrawingFrame2D.cs` の所有権を維持する。一回描画の PreparedDraw2D は完了と command 破棄後に Dispose し、retained frame と同じ型・手順とみなさない。retained は未記録なら Dispose、記録後で確実に未送信なら command 破棄後に AbortUnsubmitted を使う。

待機キャンセルは GPU の取消しではない。送信後は actual と owner を保持して完了確認を再開する。Submit が例外を投げて受理が不明な場合、未送信扱いにせず command・frame・参照資源を隔離し、実完了または確認済み device 世代退役まで保持する。一般の Dispose や失敗した submission を正常完了の証拠にしない。

不変 geometry と共通引数表は読取り lease で共有でき、frame ごとの書込み領域は分離する。更新は別版または copy-on-write とし、古い frame の生存領域を上書きしない。既存の host frame 数と max-in-flight 上限を変更しない。構築成功時だけ bindings.ResourceLease を移動し、部分失敗で取得した私有資源は factory が回収する。回収失敗の owner と retry を失わず、caller 所有資源の二重解放を避ける。

### 同じ renderer の Graph 接続

Graph 呼出し側も構成済み IRenderer2D を渡すだけとする。基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/Drawing2DRenderGraphExtensions.cs` の Append2D は RenderGraphRenderer2D を受け取り、`src/Graphics/Lumyte.Graphics.Drawing2D.RenderGraph/RenderGraphRenderer2D.cs` が命令表を保持する。本提案では既存入口を維持し、共通 interface を受け取る overload を追加する。基準 API が既に interface を受け取るという意味ではない。

```diff
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public static class Drawing2DRenderGraphExtensions
     {
+        // 同一 device・対応 renderer を Graph 変更前に検証する。
+        // owner は GPU 完了、command と compiled graph の破棄後に解放する。
+        public static ValueTask<PreparedDrawingPasses2D> Append2D(
+            this Lumyte.Graphics.RenderGraph.RenderGraph graph, IRenderer2D renderer,
+            RenderGraphDrawList2D list, RenderTargetBinding2D target,
+            DrawingGraphBindings2D bindings, CancellationToken cancellationToken = default);
     }
 }
```

以下の Graph 経路も同じ renderer を使い、既存 Graph のコンパイル・記録・送信手順を変えない。graphList、graphTarget、bindings は同じ graph に属する既存型の入力とする。

```csharp
using Lumyte.Graphics.Drawing2D.RenderGraph;

PreparedDrawingPasses2D passes = await graph.Append2D(renderer,
    graphList, graphTarget, bindings, cancellationToken);
var compiled = graph.Compile();
compiled.Record(commands); // renderer を別途 Record しない。
commands.Finish();
var actual = queue.Submit([commands]);
await actual.WaitAsync(CancellationToken.None);
commands.Dispose();
compiled.Dispose();
graph.Dispose();
passes.Dispose();
actual.Dispose();
```

内部 descriptor と実行 bridge は Drawing2D が所有する。Drawing2D.RenderGraph は基準 SHA の `src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj` にある friend 境界から取得し、資源範囲、pass、依存、出力を既存 Graph へ宣言する。Drawing2D から Graph への参照や公開計画引数を追加しない。外部の任意 IRenderer2D 実装は隠れた protocol を満たすと約束せず、未対応なら Graph の変更前に拒否する。

基準 SHA の `src/Graphics/Lumyte.Graphics.RenderGraph/IRenderGraphPreparation.cs` と `src/Graphics/Lumyte.Graphics.RenderGraph/RenderGraphPreparationContext.cs` の live pass 情報だけでは Graph 全体の記録成否を証明できない。`src/Graphics/Lumyte.Graphics.RenderGraph/Lumyte.Graphics.RenderGraph.csproj` の friend 境界を使い、adapter 向け内部通知を追加する。

```diff
 namespace Lumyte.Graphics.RenderGraph
 {
     public sealed class RenderGraph : IDisposable
     {
+        // Compile 前に登録し compiled owner へ移動。end は例外時も必ず通知する。
+        internal void ObserveRecording(Action<IGraphicsCommandBuffer> begin,
+            Action<IGraphicsCommandBuffer, Exception?> end, Action disposed);
     }
 }
```

bridge は必要な生存 producer 集合、実際の command 同一性、全 Record の成否、callback owner の破棄を対応付ける。直接実行と Graph 実行は排他的とし、二重記録を拒否する。Append 途中失敗では graph を捨て、未送信なら callback と command の破棄後に回収する。不明な送信結果では隔離を維持する。

Graph 経路で実 submission の正常完了通知を得ない限り、cross-frame GPU residency を公開しない。culling、未実行、中断、producer の部分成功、単なる Dispose を初期化証拠にしない。fixture producer は直接経路で実完了を確認する。Graph の一時 allocation を compiled.Dispose 後まで所有すると仮定せず、テスト用出力共有には renderer 側 owner の allocation を import し、独立 lease を保持する。

## 検討した代替案

- 命令別表だけをジェネリックにする: 従来型の局所交換には適するが、全シーン compute と方式専用 IR を表せない。表は従来型の構成部品として残す。
- 既定値付き preset: 簡単に開始できるが、実処理の選択と欠落を隠す。全段階と設定を必須とする。
- 描画側に段階の実行を公開する: 中間入力は直接渡せるが、方式ごとに手順・資源寿命が変わる。固定入力も renderer を構築する境界へ置く。
- 一つの mode 列挙で内部を選ぶ: 選択は簡単だが、型付き独自部品・分岐・join を表せない。Composition の children に実部品を列挙する。
- 全方式を共通 CPU 平坦化と固定段階へ正規化する: 境界は揃うが、生曲線からの GPU 処理と strip 型を制約する。論理入力だけを共通にする。

## 結果と影響

renderer の作者は異なる粒度の部品を構成でき、アプリケーションは生成された IRenderer2D と既存の描画・寿命手順を使える。共通 interface と Graph overload は公開追加であり、既存 concrete constructor・引数表世代・機能 lease・custom shader ABI・source device 検査を維持する。構築用公開型は増えるが、通常の呼出しへ中間形式や実行 context を持ち込まない。

### 意味の保存と受け入れ条件

[GRAPHICS-0033 の数値規則](GRAPHICS-0033-drawing2d-normalized-clips.md#数値規則と-16-サンプル相関)の標準 CPU 経路は維持する。専用方式も描画順、NonZero・EvenOdd、stroke・fill、group opacity、同一位置の shape・paint・clip の 16 サンプル相関を保存する。CPU/GPU predicate、曲線近似、attachment の描画ごとの丸めと最終丸めの差は数値契約へ明記し、全方式の bit 一致を無条件に約束しない。

一描画のサンプルを平均してから SrcOver する場合と、全描画をサンプルごとに合成して最後に平均する場合は一致しない。全 compute の融合でもこの違いを扱い、品質条件を満たせない構成は拒否する。原点、DPR、格子、色、premultiplied alpha と target の Load/Clear を勝手に変更しない。

- 同じ Canvas・list・snapshot・read lease の consumer コードを別 assembly から検証し、全構成の最終返り値が IRenderer2D であることを公開署名から追跡する。
- 空宣言、設定欠落、未処理 route、循環、孤立出力、未解決 hazard を GPU 確保前に拒否し、DescribeDataflow の algorithm ノードが登録した全ノードと一致して暗黙追加がないことを検査する。cache・route 選択時は実 pass と半順序も照合する。
- 閉じた generic 部品定義の正しい組合せを別 assembly からコンパイルする。異種 children の geometry/text 不一致は host compile で GPU 操作前に拒否し、shader ABI・格子・数値条件も検査する。
- fixture の入力一致・不一致、独立 oracle による geometry・mask・色の readback、upload と consumer の境界を検証する。checksum の一致だけでは合格にしない。
- 疎な更新、全面更新、半透明描画間への挿入、文字・clip・paint・group opacity、CPU/GPU 丸め差と宣言した許容差を検証する。
- 全 fast、全 fallback、混在、全候補非対応、候補の適格性重複時の先着優先、不正入力・実失敗で fallback しないことを検査する。
- 参照の循環・dangling・schema 不一致・producer 支配違反、枝入力 manifest の一致、一描画の多 packet 展開、半透明 A/Path/B の ordinal 合流を検査する。
- 複数 frame、入力の変更・破棄、copy-on-write、device 世代、未実行・culling・記録失敗・待機キャンセル・未知送信・隔離退役と Graph の二重記録拒否を検証する。
- 正数予算、offset/count の checked 演算、stroke の cap/join/miter と暗黙閉辺を含む展開量、bin/scan/scratch の累積上限を確保前に検証する。上限直前・一致・超過と factory 部分失敗の回収 retry を試す。

閉じたジェネリック構成を AOT の対象にし、無制限な実行時コード生成は前提にしない。実装・性能結果は本判断に含めない。Markdown 検査は C# コンパイルや GPU の受け入れ試験を代替せず、driver の可視性と rasterization は対応 backend の実機検証を必要とする。
