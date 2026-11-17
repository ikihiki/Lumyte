# ADR-GRAPHICS-0040: 描画種別ごとの宣言を独立した処理 instance に構築する

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `06c3f3d71aab42d4913d4634673fdc8e0162e495`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0018 の命令別配送](GRAPHICS-0018-2d-graphics-engine.md#命令別interfaceとdi)
- 変更対象: renderer の構築に受動的な Composition 宣言と独立した処理 instance を追加する。
- 継続する判断: Canvas、命令別 interface、CPU clip の数値規則、描画順、準備結果と資源所有権を維持する。

## 背景

renderer の作者は矩形とパスなどのアルゴリズムを別々に設定しつつ、共通の処理状態を一つの instance にまとめたい。構築構文へ実行中の backend や所有権の接続部品を直接渡す方式では、設定と GPU 資源の寿命を分離できない。

基準コミット 06c3f3d71aab42d4913d4634673fdc8e0162e495 の src/Graphics/Lumyte.Graphics.Drawing2D/Execution/Renderer2D.cs の PrepareAsync と DrawingCommandRenderers2D は 11 種類の interface へ連続命令を配送する。同じ SHA の Renderer2D.Retained.cs の IsBuiltinRenderer は組込み具体型を識別し、TakeBuiltinBatch と PreparedRecordBatch2D により種別をまたぐ記録列を作る。全処理を wrapper で包むとこの結合を失う。

同じ SHA の src/UI/Lumyte.UI/Compose.Widget.cs の Widget は設定と子構成を持ち、CreateInstance が別型の UiElement を作る。Compose.Rectangle.cs の Rectangle と RectangleElement.cs の RectangleElement も構築構文の型と実処理の型が別である。この責務分離を採用し、UI assembly への依存や UI の反応性・mount 更新は追加しない。

## 決定

構築構文は受動的な宣言だけを保持し、独立した RendererBuildHost2D が全 11 Slot を一つの固定配送表と資源管理へ構築する。同じ宣言参照だけを一つの構築内で共有し、通常の描画側は従来どおり IRenderer2D と既存の準備結果を使う。

### 利用者の変更前・変更後

変更前は基準コードの固定表を constructor に渡す。device、target、list、cancellationToken は呼出し側が用意する。

~~~csharp
using Lumyte.Graphics.Drawing2D;

using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
~~~

変更後は全 11 種類を明示し、複数候補は Slot の大括弧内へ直接優先順に並べる。次は今回追加する生成 factory による完全な Slot 構成である。resources は呼出し側が選択した DrawingResources2D、resourceOwner と validateResources は既存と同じ所有者と検証 callback である。単機能の標準アルゴリズムには追加設定がないため空引数で宣言する。FastRectangles とその処理型の実装は「同種別内だけの候補選択」で示す。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

var bounds = new DrawingRetentionOptions2D
{
    MaxFramesInFlight = 3,
    MaxCachedAssets = 512,
    MaxCachedBytes = 64 * 1024 * 1024
};
var declaration = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: bounds)
[
    Renderer.FillRect()[new FastRectangles(), StandardFillRect()],
    Renderer.FillRoundedRect()[StandardFillRoundedRect()],
    Renderer.FillCircle()[StandardFillCircle()],
    Renderer.DrawLine()[StandardDrawLine()],
    Renderer.FillPath()[StandardFillPath()],
    Renderer.StrokePath()[StandardStrokePath()],
    Renderer.DrawImage()[StandardDrawImage()],
    Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
    Renderer.DrawDistanceField()[StandardDrawDistanceField()],
    Renderer.DrawMesh()[StandardDrawMesh()],
    Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]
];
using RendererBuildHost2D host = declaration.CreateHost();
using IRenderer2D renderer = host.BuildRenderer(device, resources, resourceOwner, validateResources);
using PreparedDraw2D prepared = await renderer.PrepareAsync(list, target, cancellationToken);
// Record、Finish、Submit、完了確認、command の破棄後に prepared を破棄する。
~~~

構築と回収がともに失敗した場合、host が未公開 instance の再試行責任を保持し、同じ host の BuildRenderer または Dispose で再試行する。再試行できる所有者を失わないよう、宣言上の BuildRenderer や一時的な自動破棄 host の拡張メソッドは設けない。構築済み renderer の所有権は host から独立する。

### 宣言、名前付き Slot と実処理

Definitions.Renderer は必須方針と子宣言の topology だけを保持し、device、資源 snapshot、処理 instance、未回収の native owner、Dispose、PrepareAsync、Record は持たない。CreateHost は環境をまだ固定せず独立した構築 host を作る橋渡しであり、実構築を宣言へ委譲しない。

PipelineDeclaration2D は受動的な設定の基底型で、`IPipelineDeclaration2D<TCommand>` は処理メソッドを持たない能力 marker である。CreateElement は構築時だけ呼ばれ、宣言と異なる処理型を返す。独自の所有処理型は PipelineElement2D を継承し、従来の命令別 interface を実装する。PipelineElement2D が構築 identity と採用済み宣言を保持し、別構築で既に採用した処理 instance の再返却を拒否する。既存 callback には新しい資源保持プロトコルを要求しない。

標準宣言は既存の sealed DefaultFillRectRenderer2D などを構築時に新規作成し、実 instance を固定表へ直接配置する。標準処理を包む wrapper は挿入しない。互換用途だけに `BorrowedPipeline2D<TCommand>` を残し、caller 所有の既存 callback を借用できる。通常の Slot 構成に借用 token や共有 factory は不要である。

既存の ComposeSlot と CompositionSlotAssignment を使い、各 Slot の子型を対応 command の `IPipelineDeclaration2D<TCommand>` にする。異なる種別の子や別対象の assignment は C# の型検査で拒否する。全種別に一つ以上の宣言を順に明示し、CreateDefault や preset で不足を補わない。null、同一 Slot 内の同一宣言参照、消去を挟まない二重代入は適用時に全体を検証して拒否し、部分置換しない。空 children は Slot を消去する。異なる種別間では同じ宣言参照を共有できる。凍結前なら明示的な消去後に再代入できる。

配列は既存 Core の機構が捕捉時と適用時にコピーし、同じ assignment を独立した複数の root へ使える。共有宣言へ UI の一親制約は適用しない。最初の DescribeDataflow または構築が宣言 topology を凍結し、欠落、無効な予算・引数表方針、同名で異なる宣言、処理型と Slot 能力の不一致を処理生成前に拒否する。宣言の DescribeDataflow は device を必要とせず、生成も predicate 評価もしない。

host は最初の検査・構築で device と DrawingResources2D の snapshot を固定し、異なる環境と破棄後の利用を拒否する。同じ凍結宣言から独立した host を作れば別 device 世代にも構築できる。設定変更、構築と描画は利用側が直列化する。宣言は GPU 所有者でないため IDisposable を実装しない。

### 独自宣言と複数種別の共有

次は外部 assembly の作者が用意する宣言と処理の最小例である。宣言は資源を持たず、別型の PathsProcessor が二つの interface を処理する。標準処理への委譲は例を短くするためで、独自 shader の確保と解放は処理型の責務になる。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;

public sealed class Paths : PipelineDeclaration2D,
    IPipelineDeclaration2D<FillPathCommand2D>, IPipelineDeclaration2D<StrokePathCommand2D>
{
    public override Type ProcessorType => typeof(PathsProcessor);
    protected override object CreateElement(PipelineBuildContext2D context) => new PathsProcessor();
}

public sealed class PathsProcessor : PipelineElement2D, IFillPathRenderer2D, IStrokePathRenderer2D
{
    private readonly IFillPathRenderer2D _fill = new DefaultFillPathRenderer2D();
    private readonly IStrokePathRenderer2D _stroke = new DefaultStrokePathRenderer2D();

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
        ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
        => _fill.PrepareAsync(context, commands, cancellationToken);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
        ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
        => _stroke.PrepareAsync(context, commands, cancellationToken);
}

var paths = new Paths();
var fill = Renderer.FillPath()[paths];
var stroke = Renderer.StrokePath()[paths];
// この二つと、残り九種別を明示した assignment を同じ root に適用する。
~~~

同じ宣言参照から一つの構築内で実処理を一度だけ作る。別の BuildRenderer では独立した処理を作り、同型でも異なる宣言を自動統合しない。名前は診断と衝突検査にだけ使い、型 singleton、global registry、弱参照による統合はしない。子宣言を持つ独自アルゴリズムは GetChildDeclarations で受動的な子の topology を列挙し、構築専用 PipelineBuildContext2D.GetProcessor でその子を解決できる。名前衝突と循環は子を含む全構成で生成前に検査し、列挙されていない子の生成は拒否する。context の解決権限は CreateElement の終了時に閉じ、成功・失敗・renderer 破棄後に遅延生成できない。

論理宣言と物理所有者の identity は別である。異なる宣言が同じ物理 owner を返す場合、同一構築内では解放責任だけを参照 identity で一つにする。resourceOwner と処理 owner の同一実体も成功時だけ外部 owner の経路へ移し、失敗時は caller 所有権を消費しない。ただし未公開の依存処理の回収が失敗した間は再採用の予約を保持し、回収完了後にだけ予約を返す。異なる処理 owner は依存する外部資源 owner より先に破棄する。

### 同種別内だけの候補選択

Slot の大括弧へ受動的なアルゴリズム宣言を優先順に直接並べる。候補を包む構築型や候補設定 token は設けない。次の宣言は設定だけを保持し、別型の処理が任意の型付き admission を実装する。

~~~csharp
public sealed class FastRectangles : PipelineDeclaration2D,
    IPipelineDeclaration2D<FillRectCommand2D>
{
    public override Type ProcessorType => typeof(FastRectanglesProcessor);
    protected override object CreateElement(PipelineBuildContext2D context)
        => new FastRectanglesProcessor();
}

public sealed class FastRectanglesProcessor : PipelineElement2D,
    IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
{
    private readonly IFillRectRenderer2D _algorithm = new DefaultFillRectRenderer2D();
    public bool CanPrepare(RenderPreparationContext2D context,
        ReadOnlyMemory<FillRectCommand2D> commands)
    {
        foreach (FillRectCommand2D command in commands.Span)
        {
            if (command.Paint.Root is not SolidPaintNode2D ||
                command.State.Transform != System.Numerics.Matrix3x2.Identity ||
                command.State.Clip.Elements.Count != 0) return false;
        }
        return true;
    }
    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context,
        ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        => _algorithm.PrepareAsync(context, commands, cancellationToken);
}

var slot = Renderer.FillRect()[new FastRectangles(), StandardFillRect()];
~~~

host は全候補を構築時に解決し、必要な Slot だけを内部の候補配送処理へ下げる。論理 kind の固定配送後、従来と同じ clip を共有する連続同種 batch 全体に対し、その Slot の CanPrepare だけを先頭から評価する。最初の true を一度だけ PrepareAsync し、すべて false なら NotSupportedException とする。batch 全体の判定であり、命令ごとの分割を約束しない。単一候補でも admission を省略しない。

admission を実装しない従来の callback は batch 全体に対応する契約を維持する。単一の標準宣言は既存 sealed instance を包まず直接配置する。これはそのアルゴリズム自身の全対応能力であり、不足 Slot の補完や隠れた標準 fallback ではない。複数候補の内部配送処理は子処理を借用し、子の所有 scope と二重解放しない。

predicate は GPU 確保・書込み・記録・送信をしない純粋な判定である。predicate の例外、不正入力、資源失効、実確保失敗、device failure を非対応へ変換せず、実処理の失敗後に次候補へ進めない。paint、clip、transform を含む実入力で判断し、矩形を path に変換する場合も同じ塗り領域と数値順序を維持する。基準 SHA の Model/FillRectCommand2D.cs は Rect/Paint/State、StrokeStyle2D.cs は width/cap/join/miter を持ち、存在しない dash を推測しない。

### 資源・記録・Graph の継続契約

renderer ごとの DrawingResourceState2D、RetainedDrawingCache2D と所有者 ledger は一つである。DrawingArgumentPlan2D は各 PrepareAsync ごとに一つで、全種別が同じ context へ登録して最後に SealAsync する。frame ごとの可変 scratch、mask、出力、登録中の引数表は共有しない。読取り geometry の世代共有、copy-on-write、table reference lease を維持する。

基準 SHA の Renderer2D.cs、Renderer2D.Retained.cs と PreparedRecordBatch2D.cs の順序を維持し、論理命令順に record、geometry、paint と clip を準備して共通引数計画を確定する。Record が転送と draw を記録し、従来の fragment が形状 coverage と paint を融合評価する。独立した Coverage prepass が存在すると説明しない。互換な連続組込み命令は種別をまたいで結合し、custom や choice を組込みと誤認して bypass しない。Painter order と clip 境界を維持する。

構築 scope は direct の準備結果、retained batch run、Graph の準備 owner の使用権から保持する。custom batch が context.RetainResources を呼ばなくても、その batch の回収成功まで所有処理を保持する。全 batch の回収成功前に使用権を返さず、失敗 owner は再試行可能な経路に残す。既存 RetainResources の任意性と custom 構成での shader の遅延確保を維持する。

Graph は IRenderer2D の overload を使い、Drawing2D 内の friend interface が同じ固定表、resource coordinator と ArgumentTableMode を接続する。未対応の外部 renderer と異なる device は graph 変更前に拒否する。source の破棄後も有効な Graph host の借用を typed command、layer、backdrop、capture へ伝えるが、実借用なしに閉じた owner の guard を緩めない。失敗した Append の回収を先に再試行してから host 借用を返す。

元の renderer とそれから借用した Graph host の準備は利用側が直列化する。retained frame は Record、Finish、Submit、AttachSubmission、実完了確認、command 破棄、Retire、Dispose の順に扱う。未記録は Dispose、記録済みで確実に未送信なら command 破棄後に AbortUnsubmitted を使う。待機キャンセルを GPU の取消しとみなさず、受理不明なら実完了または確認済み device 世代退役まで隔離保持する。

### 公開 API 差分

以下は基準コミットからの追加・変更の抜粋である。factory、required ComposeParameter、Slot と assignment indexer は既存 generator だけを使う。非同期 factory や通常描画側の stage runner は追加しない。

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
+        public IGraphicDevice Device { get; } // 借用 device。
         // 既存の 5 引数・6 引数 constructor と準備・破棄の契約を維持。
     }
 }
+namespace Lumyte.Graphics.Drawing2D.Pipelines
+{
+    public interface IPipelineDeclaration2D<TCommand> where TCommand : DrawingCommand2D
+    {
+        PipelineDeclaration2D Declaration { get; }
+    }
+    public abstract class PipelineDeclaration2D
+    {
+        public PipelineDeclaration2D Declaration { get; }
+        public virtual string? Name { get; }
+        public abstract Type ProcessorType { get; } // 純粋な metadata。
+        protected abstract object CreateElement(PipelineBuildContext2D context);
+        protected virtual IEnumerable<PipelineDeclaration2D> GetChildDeclarations();
+    }
+    public abstract class PipelineElement2D
+    {
+        public PipelineDeclaration2D? Declaration { get; } // 実処理側の採用済み設定。
+    }
+    public sealed class PipelineBuildContext2D
+    {
+        public IGraphicDevice Device { get; }
+        public TProcessor GetProcessor<TProcessor>(PipelineDeclaration2D declaration)
+            where TProcessor : class;
+    }
+    public sealed class RendererBuildHost2D : IDisposable
+    {
+        public RendererBuildHost2D(DrawingCompose.Definitions.Renderer declaration);
+        public DrawingCompose.Definitions.Renderer Declaration { get; }
+        public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow(
+            IGraphicDevice device, DrawingResources2D resources);
+        // 成功時だけ owner を引き取る。失敗時の回収を host が保持する。
+        public IRenderer2D BuildRenderer(IGraphicDevice device, DrawingResources2D resources,
+            IDisposable? resourceOwner, Action? validateResources);
+        public void Dispose(); // 未公開処理の回収を再試行。構築済み renderer は独立。
+    }
+    public interface IPipelineAdmission2D<TCommand> where TCommand : DrawingCommand2D
+    {
+        // 凍結設定と入力だけを読む。GPU 書込み・記録・送信は禁止。
+        bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<TCommand> commands);
+    }
+    public static partial class DrawingCompose
+    {
+        public static Definitions.RendererFactory Renderer { get; } // 生成 factory。
+        public static partial class Definitions
+        {
+            [Composable(Factory = "DrawingCompose")]
+            public partial class Renderer
+            {
+                [ComposeParameter] public required DrawingRetentionOptions2D Retention { get; init; }
+                [ComposeParameter] public required DrawingArgumentTableMode ArgumentTables { get; init; }
+                public Renderer this[params CompositionSlotAssignment<Renderer>[] content] { get; }
+                public RendererBuildHost2D CreateHost();
+                public IReadOnlyList<CommandRouteDescription2D> DescribeDataflow();
+                [ComposeSlot] private static void FillRect(Renderer target,
+                    IReadOnlyList<IPipelineDeclaration2D<FillRectCommand2D>> children);
+                // 残り 10 Slot も完全な利用例の種別名と command 型に対応する。
+            }
+            [Composable(Factory = "DrawingCompose")]
+            public sealed partial class StandardFillRect : PipelineDeclaration2D,
+                IPipelineDeclaration2D<FillRectCommand2D>;
+            // 残り 10 種別も対応する標準宣言を持つ。
+        }
+    }
+    public sealed class BorrowedPipeline2D<TCommand> : PipelineDeclaration2D,
+        IPipelineDeclaration2D<TCommand> where TCommand : DrawingCommand2D
+    {
+        public BorrowedPipeline2D(object processor); // 明示的な caller 所有 callback の互換用途。
+    }
+}
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public sealed class RenderGraphRenderer2D
     {
+        // 同じ固定表と資源管理を借用。未対応実装は graph 変更前に拒否。
+        public RenderGraphRenderer2D(IRenderer2D renderer, RenderGraphDrawList2D list,
+            RenderTargetBinding2D target, DrawingGraphBindings2D bindings);
     }
 }
~~~

生成される Renderer.FillRect() は `CompositionSlot<Definitions.Renderer, IPipelineDeclaration2D<FillRectCommand2D>>` を返す。children indexer は `CompositionSlotAssignment<Definitions.Renderer>` を返し、root の indexer が順に適用する。処理 interface や実行 binding を child 型にしない。

DescribeDataflow は実際の凍結 route 配列から kind、command 型、処理 interface、宣言名、宣言された処理型、候補順序と CPU・転送・GPU 契約を返す。predicate や CreateElement は実行せず、実行済み pass 数、入力ごとの選択結果、転送量は推測しない。

## 検討した代替案

- 宣言を実行 interface の wrapper や factory 接続部品に改名する: 表面だけ変わり設定と実処理を分離できない。受動的な宣言と独立した host・処理型を使う。
- 全描画を全 root の候補列へ渡す: 無関係な種別の判定も呼ぶため先に固定 kind 配送を行う。
- 種別ごとに Renderer2D を作る: 共通引数表と順序付き記録の結合を失うため一つの coordinator へ構築する。
- 同型 manager を暗黙共有する: 設定、device 世代、所有者が異なる実体を誤統合するため宣言参照だけを共有する。
- UI の更新機構を導入する: 必要なのは宣言と実処理の分離であり、反応性や mount 更新は今回の責務でない。

## 結果と影響

構築側は全種別と独立した host の寿命を明示するが、描画側は既存 Canvas、IRenderer2D と準備結果を使える。構築構文の型は GPU 資源を所有せず同じ宣言から独立した処理を作り直せる。組込みの種別横断結合と既存 custom replacement を維持する。

受入れ条件は、全生成 Slot の外部 assembly 利用、公開 signature からの実行 binding の不在、宣言時の生成・predicate 呼出しゼロ、別型の実処理生成、共有と独立構築、途中失敗と回収再試行、元 interface の実呼出し、組込み結合と pixel oracle、direct・retained・Graph の資源寿命、AOT と trimming で確認する。

範囲は現行 11 種別の宣言・構築・処理境界である。全 Path 入力の一括評価、任意 CPU/GPU DAG、Fork/Join、Compute/Sparse 処理 engine は含めない。性能値や速度向上は保証しない。
