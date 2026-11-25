# ADR-GRAPHICS-0041: 完了済みの不変描画資源を複数フレームで保持する

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `2ea92f46b0b9332f62e8208294e89111551d9ce6`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0040 の型付き接続と寿命の共通契約](GRAPHICS-0040-composable-drawing-pipelines.md#型付き接続と寿命の共通契約)
- 変更対象: 組込み描画の不変 GPU payload の同時共有と明示的な保持方針を追加する。
- 継続する判断: CPU scene、型付き Stage/Then と明示 Upload、準備ごとの処理 instance、描画順、数値規則、完了後の回収を維持する。

## 背景

保持型の描画を使う利用者は、前のフレームが GPU で処理中でも、変わらない形状と塗りの資源を次のフレームで使いたい。排他的な再利用だけでは同時に準備するフレームごとに同じ不変資源を作り直すため、編集内容と進行中の GPU 資源の寿命を分離する必要がある。

基準コミット 2ea92f46b0b9332f62e8208294e89111551d9ce6 の src/Graphics/Lumyte.Graphics.Drawing2D/Execution/RetainedDrawingCache2D.cs の Take は idle asset を取り出し、ReleaseFrame は確認済み完了後に返す。同じ SHA の Renderer2D.Retained.cs は解決済み Transform と Clips を key に含め、PreparedRecordBatch2D.cs は record、数値列、paint と metadata を引数表に登録する。CPU の DrawingScene2D と immutable DrawList2D はそのまま編集内容の正本とする。

## 決定

明示的な保持方針を選んだ renderer は、初期化が完了した組込み描画資源を読み取り専用の所有者として共有する。利用者は既存の factory と PrepareAsync を使い、各フレームの target、command、submission と完了確認を独立させる。既定の排他的な再利用と custom callback は変更しない。

### 利用者の変更前・変更後

変更前は GRAPHICS-0040 の GraphicsPipelineBuildHost が各準備の Upload 用に新しい buffer を作る。変更後は同じ定義、明示 Upload と直接渡す IPipeline を保ち、GraphicsPipelinePolicy.ImmutableUploads に有限の保持方針を指定する。下の execution、device、artifacts、definition、routes、snapshot、target は caller が保持する既存の値である。definition は Normalize、明示 Upload、具体的な独立版または融合版を同じ Stage/Then で結ぶ。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;

var policy = new GraphicsPipelinePolicy(64 * 1024 * 1024,
    128 * 1024 * 1024, 64 * 1024 * 1024, 8)
{
    ImmutableUploads = new GraphicsImmutableUploadPolicy(
        maxBytes: 64 * 1024 * 1024, maxChunks: 512, chunkBytes: 65536),
};
using var build = new GraphicsPipelineBuildHost(execution, device, artifacts, policy);
using var pipeline = build.Build(definition);
var retention = new DrawingRetentionOptions2D
{
    EnableResidentInstances = true,
    MaxFramesInFlight = 3,
    MaxCachedBytes = 64 * 1024 * 1024,
    MaxWorkingBytes = 256 * 1024 * 1024,
};
using IRenderer2D renderer = new Renderer2D(device, pipeline, routes,
    new PipelinePolicy2D(policy, DrawingArgumentTableMode.Auto, retention));
using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, target);
frame.Record(commands); // caller が target の初期 barrier を先に記録する。
commands.Finish();
using var submission = device.Queue.Submit([commands]);
frame.AttachSubmission(submission);
await submission.WaitAsync();
commands.Dispose();
frame.Retire(); // この確認済み境界だけで不変 Upload を後続準備へ公開する。
~~~

既存 constructor と DrawingCompose の互換 factory は維持するが、汎用段階の中へ既存 renderer 全体を包まない。ImmutableUploads が null の Graphics build は従来どおり毎回転送する。汎用 Renderer2D が EnableResidentInstances を選ぶ場合は、build に固定した明示 ImmutableUploads を必須とし、その native byte 上限が Retention.MaxCachedBytes を超える組合せを構築前に拒否する。

UiApplicationOptions.DrawingRetention に同じ factory の値を明示すれば、通常の presenter、candidate と visual-image の既存経路にも適用する。MaximumFramesInFlight と方針のフレーム上限は一致させる。null の既定では従来の構築を維持する。

### 共有する内容と更新

汎用経路の共有所有者は、明示 Upload がコピーした規範要素列の読み取り専用 native chunk を保持する。共有範囲は一つの compiled pipeline、device reference と generation に閉じ、node、schema 型・version・layout・metadata、要素数と規範 byte の SHA-256 identity を照合する。source の revision 整数だけで判定しない。同一 byte の storage 再利用は source metadata の同一性を偽装せず、毎回の新しい Device lease が当該準備の source identity と実資源 snapshot を保持する。ホストの借用可変メモリ、target、writable scratch、実行途中の plan は cache に入れない。互換経路は従来の native record、geometry、paint と引数表の世代を一体で保持する。CPU 配列だけを保持した状態を GPU 常駐とは呼ばず、最終 raster image のキャッシュも作らない。同じ外部 texture view の新しい内容は次の raster で読む。

各 PreparedDrawingFrame2D は content page identity、解決済み transform、clip、順序、target の寸法・形式、DPR と device 世代に対応する固定資源を持つ。revision の整数だけでは判定しない。node の slot 再利用、削除・再挿入、非表示、祖先の変更は新しい frame の順序と解決済み値に反映する。live UI object と scene read lease は戻り値に捕捉しない。

汎用 Upload は最大 ChunkBytes の要素境界で規範 byte 列を分割し、変更 chunk だけを別の native owner に構築する。読み取り専用 buffer は ShaderWrite usage を持たない。同じ chunk の内容を更新して古い frame に見せない。ABI が連続 buffer を要求する複数 chunk の出力では、各準備に固有の連続 buffer へ GPU copy して結合する。その結合先と scratch は保持しない。互換経路の最大 4 CPU pages、最大 256 logical commands という単位とは区別し、両者の無効化粒度を同じだと主張しない。

整数平行移動は DPR=1、command transform が identity、実 clip なし、solid paint、原点ゼロの target、canonical bounds が余白込みで target 内にある場合に限定する。target extent と移動量は 8192 以下とする。汎用経路は全 entry が同じ適格な移動を持つ準備だけで、scene の不変 geometry をローカル座標のまま Upload し、明示した準備 metadata の TranslationX/Y を stage の反映済み引数へ渡す。独立 coverage/paint/clip と融合評価は全 16 sample の評価位置から同じ整数移動を引く。mesh の center 評価規則は変えない。異なる entry の移動、小数移動、scale、DPR、clip、複雑な paint は通常の canonical 再構築に残し、例外後の fallback は行わない。互換 shader の 64 byte root 予約領域の property は既存のままとする。

既存 GetScissor、stroke の演算順、mesh coverage、premultiplied display-linear paint、4×4 相関 sample と painter order は変更しない。frame ごとの可変な引数計画と scratch は共有せず、完了後の読み取り専用世代だけを lease で保持する。

### 初期化、完了と予算

Record は residency を公開しない。初回の同じ frame 内では、同じ command buffer に先行 producer が実際に成功して記録された後だけ、その初期化結果を alias が使える。別の未完了 frame へは未初期化資源を共有しない。

成功した Submit の実 submission を AttachSubmission し、GPU 完了と command の破棄を確認して Retire したときだけ、後続 frame が再利用できる。待機キャンセルと受理不明の Submit は完了とみなさず、既存の確認済み回収まで隔離する。未記録は Dispose、記録済みで確実に未送信の場合だけ command 破棄後に AbortUnsubmitted を使う。最後の lease の回収に失敗した場合、既存 owner の再試行経路を維持する。

一つの renderer の resource coordinator と retained cache が frame を管理し、compiled pipeline が不変 Upload の cache を所有する。MaxBytes は active・未公開・idle を含む native chunk の有限上限、MaxChunks は hash/metadata を含む所有者数の有限上限である。入場時に checked arithmetic で検査し、idle のみ回収しても足りなければ native 確保前に拒否する。run の staging と連続結合先は別の既存 MaxScratchBytes/MaxTransferBytes に保守的に計上する。RenderRequest2D は capture と snapshot の課金後に残る MaxWorkingBytes を IGraphicsPreparationBudget.MaxPreparationBytes として明示し、Graphics host は compiled MaxScratchBytes とその小さい方で CPU/GPU の実準備を確保前に検査する。この任意の汎用契約は上限を引き下げるだけであり、compiled topology や capability を変更しない。最後の使用 lease が戻るまで pipeline の Dispose も native owner を回収しない。共有 native owner は重複計上せず、各 lease と frame metadata を追加計上する。MaxCachedBytes は idle 保持の予算であり、GPU 全体の VRAM 上限ではない。active と隔離中の資源は予算達成のために破棄しない。MaxWorkingBytes は保持経路の capture、変換、compiled owner、staging と page 確保の保守的な上限であり、cache hit も frame 全体の計上から除外しない。

### 同じ親 Graph への接続

AppendResident2D は既存 PreparedDrawingFrame2D と imported target を親 Graph の一つの pass へ接続する。frame と資源の所有権は caller に残し、Graph の破棄自体では residency を公開しない。target と sampled binding を検証してから pass を追加する。transient target と late-bound allocation は追加前に拒否する。

同じ Append 内の全 generic segment は一つの MaxWorkingBytes を共有する。準備済み run の固定された host/device 作業予約と source snapshot を owner identity ごとに一度だけ計上し、次の segment の source 配列・target binding・native scratch を準備する前に、既存 owner と共有 argument plan の課金を差し引く。最後の合計検査だけで代用せず、残予算が足りない stage の native 確保前に拒否する。generic batch も既存の作業予算・所有者列挙の内部契約に参加し、互換 owner と同じ identity が現れても重複課金しない。

plan.Record、Finish、Submit、AttachSubmission、実完了確認、command と plan の破棄、frame.Retire の順序を使う。初期化と raster を同じ pass に置き、producer だけを cull しない。nested Graph、新しい Canvas、editable GPU scene tree は導入しない。

### 公開 API 差分

基準コミットからの追加は以下である。GraphicsPipelinePolicy に nullable な ImmutableUploads init property を追加し、GraphicsImmutableUploadPolicy は MaxBytes、MaxChunks、ChunkBytes を不変捕捉する。GraphicsPipelineRecordPlan.RetireImmutableUploads(actualSubmission) は直接 Graphics 利用者向けの明示的な完了通知であり、同 plan の成功記録、実 submission の完了、記録 command の破棄、元 device の Ready を検証する。caller はその command を含む実 receipt を渡す契約を負い、無関係な完了 receipt を渡してはならない。Drawing2D の Retire は同じ bridge に既存 AttachSubmission の receipt を渡す。GraphicsPipelineExecutionHost の正常終了も全 epoch の実完了と command 回収後だけ同じ公開を行い、取消し・隔離・世代退役による回収では公開しない。

既存 IRenderer2D、3 種類の PrepareAsync、5・6 引数 constructor と全命令別 interface を維持する。

~~~diff
+namespace Lumyte.Graphics.Pipelines
+{
+    public sealed record GraphicsImmutableUploadPolicy
+    {
+        public GraphicsImmutableUploadPolicy(long maxBytes, int maxChunks,
+            int chunkBytes = 65536);
+        public long MaxBytes { get; }
+        public int MaxChunks { get; }
+        public int ChunkBytes { get; }
+    }
+    public sealed record GraphicsPipelinePolicy
+    {
+        public GraphicsImmutableUploadPolicy? ImmutableUploads { get; init; }
+    }
+    public interface IGraphicsPreparationBudget
+    {
+        long MaxPreparationBytes { get; }
+    }
+    public sealed class GraphicsPipelineRecordPlan
+    {
+        public void RetireImmutableUploads(IGraphicsSubmission submission);
+    }
+}
 namespace Lumyte.Graphics.Drawing2D
 {
     public sealed record DrawingRetentionOptions2D
     {
+        // 既定 false。組込み immutable payload の同時共有を明示する。
+        public bool EnableResidentInstances { get; init; }
+        // resident preparation の保守的な作業上限。既定 256 MiB、正数必須。
+        public long MaxWorkingBytes { get; init; }
     }
 }
 namespace Lumyte.Graphics.Drawing2D.Pipelines
 {
     public static partial class DrawingCompose
     {
+        public static DrawingRetentionOptions2D RetentionGpuLimits(int maxFramesInFlight,
+            int maxCachedAssets, long maxCachedBytes, long maxWorkingBytes);
     }
 }
 namespace Lumyte.Graphics.Drawing2D.RenderGraph
 {
     public static partial class Drawing2DRenderGraphExtensions
     {
+        // caller が frame と imported allocation を完了・command 破棄まで保持する。
+        public static void AppendResident2D(this RenderGraph graph,
+            PreparedDrawingFrame2D frame, RenderTargetBinding2D target,
+            DrawingGraphBindings2D bindings);
     }
 }
 namespace Lumyte.UI.Application
 {
     public sealed record UiApplicationOptions
     {
+        // null は既存方針。frame 上限は application の上限と一致させる。
+        public DrawingRetentionOptions2D? DrawingRetention { get; init; }
     }
 }
~~~

## 検討した代替案

- 排他的な完了後キャッシュだけを使う: 既存の単純な所有権を維持できるが、複数の進行中 frame で同じ不変 payload を共有できない。既定には残し、明示方針で共有を追加する。
- CPU 配列または最終 raster image だけを保持する: 前者は native 登録の再構築をなくさず、後者は外部 texture の更新と配置・clip の責務を変える。既存の compiled GPU owner を保持する。
- 進行中の buffer を直接更新する: 新しい frame の更新が古い frame に見えるため採用せず、変更単位を新規構築する。

## 結果と影響

利用者は既存の宣言と描画操作を保ち、不変 native 資源を複数の完了所有者から参照できる。明示的な作業上限と completion-safe な回収責任は必要である。全 visible traversal と全 target の raster は残り、frame 全体の O(dirty)、転送ゼロ、速度向上は保証しない。

受入れ条件は、実 buffer と引数表 identity、変更単位だけの再構築、初回 alias、revision=0 の実 property 変更、古い frame の保持、上限・取消し・回収失敗、custom callback の寿命で確認する。native Vulkan と wgpu では独立した三つの submission を全て送信してから待ち、2→0→1 の順に退役させ、新規 direct 描画の全 pixel と比較する。Graph、明示 factory、host、AOT と trimming の確認はそれぞれ独立した受入れ条件とする。

方式の追加、mesh shader、GPU scene の編集、damage redraw は対象外である。

### Explicit generic paging does not extend the immutable cache

The opt-in PagedCoverage/PagedPaint/PagedClip/PagedCompose factories use the unchanged version-one canonical ports and immutable source allocation. Their private scene-page directory resolves existing absolute cell addresses; it is not retained canonical content or a new cache entry. Every output window owns a fresh table generation, RO source-page registrations, and fresh upload/device directory storage. Compute outputs remain fresh RW allocations; Compose registers their exact windows RO in different owned tables. These objects retire with the preparation/submission, never with a reused plan or global registry. Source leases, source identity, device generation, translationX/Y and source/grid matching continue to use the existing Graphics ownership path.

GenericPagingLimits2D explicitly bounds live source pages and window tables. At 64 KiB the default three pages admit at most 192 KiB of arbitrary-read SceneV1 bytes and 512 windows admit the required 257×1×256-command dense fixture (257 windows). This is not unbounded paged geometry and does not reduce the original whole-range factories' acceptance ceiling. Logical table capacity is not a physical descriptor count: three scene pages + directory + paint output + resource data + backend remap is the worst-case seven-storage-binding path. Stage preparation reserves its aggregate native scratch/transfer credit before allocating the first output or per-window resource, then consumes that credit through ordinary scoped allocation paths. No hidden Download, sample reduction, mutable cached output, or CPU fallback is introduced. See CanonicalSceneV1.md for the private binding ABI and alignment/source limits.
