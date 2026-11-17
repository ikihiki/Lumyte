# ADR-GRAPHICS-0041: 完了済みの不変描画資源を複数フレームで保持する

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `1773ef4e9c6db25aaddfac72d0143cb22a213ff8`
- 変更関係: 一部変更
- 変更元: [GRAPHICS-0040 の資源・記録・Graph の継続契約](GRAPHICS-0040-composable-drawing-pipelines.md#資源記録graph-の継続契約)
- 変更対象: 組込み描画の不変 GPU payload の同時共有と明示的な保持方針を追加する。
- 継続する判断: CPU scene、11 Slot の配送、配置ごとの処理 instance、描画順、数値規則、完了後の回収を維持する。

## 背景

保持型の描画を使う利用者は、前のフレームが GPU で処理中でも、変わらない形状と塗りの資源を次のフレームで使いたい。排他的な再利用だけでは同時に準備するフレームごとに同じ不変資源を作り直すため、編集内容と進行中の GPU 資源の寿命を分離する必要がある。

基準コミット 1773ef4e9c6db25aaddfac72d0143cb22a213ff8 の src/Graphics/Lumyte.Graphics.Drawing2D/Execution/RetainedDrawingCache2D.cs の Take は idle asset を取り出し、ReleaseFrame は確認済み完了後に返す。同じ SHA の Renderer2D.Retained.cs は解決済み Transform と Clips を key に含め、PreparedRecordBatch2D.cs は record、数値列、paint と metadata を引数表に登録する。CPU の DrawingScene2D と immutable DrawList2D はそのまま編集内容の正本とする。

## 決定

明示的な保持方針を選んだ renderer は、初期化が完了した組込み描画資源を読み取り専用の所有者として共有する。利用者は既存の factory と PrepareAsync を使い、各フレームの target、command、submission と完了確認を独立させる。既定の排他的な再利用と custom callback は変更しない。

### 利用者の変更前・変更後

変更前は GRAPHICS-0040 の完全な Slot 宣言に RetentionLimits を指定し、完了した資源を排他的に再利用する。変更後は同じ宣言の保持方針だけを RetentionGpuLimits にする。resources、resourceOwner、device、snapshot、target は既存の caller 所有値である。

~~~csharp
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto,
    retention: RetentionGpuLimits(maxFramesInFlight: 3, maxCachedAssets: 512,
        maxCachedBytes: 64 * 1024 * 1024, maxWorkingBytes: 256 * 1024 * 1024))
[
    Renderer.FillRect()[StandardFillRect()],
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
].CreateHost();
using IRenderer2D renderer = host.BuildRenderer(device, resources, resourceOwner, null);
using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, target);
// target を ColorAttachment へ遷移してから記録する。
frame.Record(commands);
commands.Finish();
using var submission = device.Queue.Submit([commands]);
frame.AttachSubmission(submission);
await submission.WaitAsync();
commands.Dispose();
frame.Retire();
~~~

上の例は command の作成、target の barrier、例外時の隔離回収を省略している。共有を選ばない変更前の方針は次であり、11 Slot と準備・完了手順は同じである。

~~~csharp
RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 512,
    maxCachedBytes: 64 * 1024 * 1024)
~~~

UiApplicationOptions.DrawingRetention に同じ factory の値を明示すれば、通常の presenter、candidate と visual-image の既存経路にも適用する。MaximumFramesInFlight と方針のフレーム上限は一致させる。null の既定では従来の構築を維持する。

### 共有する内容と更新

共有所有者は native record、geometry の数値列、paint、metadata、推移的な参照、その参照を発行した引数表の世代をまとめて保持する。CPU 配列だけを保持した状態を GPU 常駐とは呼ばず、最終 raster image のキャッシュも作らない。同じ外部 texture view の新しい内容は次の raster で読む。

各 PreparedDrawingFrame2D は content page identity、解決済み transform、clip、順序、target の寸法・形式、DPR と device 世代に対応する固定資源を持つ。revision の整数だけでは判定しない。node の slot 再利用、削除・再挿入、非表示、祖先の変更は新しい frame の順序と解決済み値に反映する。live UI object と scene read lease は戻り値に捕捉しない。

最大 4 CPU pages、最大 256 logical commands を一つの GPU 無効化単位とする。変更された page を含む単位は新しい所有者へ構築し、進行中の buffer と引数表を書き換えない。変わらない prefix と suffix は元の native payload と登録世代を保持する。互換な連続命令は CPU page 境界をまたいで結合でき、physical record 数と logical command 数は区別する。

整数平行移動は DPR=1、command transform が identity、実 clip なし、solid paint、原点ゼロの target、canonical bounds が余白込みで target 内にある場合だけ GPU property として渡す。target extent と移動量は 8192 以下とする。64 byte の既存 root の予約領域 8 byte を使い、vertex bounds と fragment の全 16 samples に同じ移動を適用する。小数移動、scale、DPR、clip、複雑な paint は資格判定で除外し、依存単位を再構築する。例外を捕捉して別方式へ再実行しない。

既存 GetScissor、stroke の演算順、mesh coverage、premultiplied display-linear paint、4×4 相関 sample と painter order は変更しない。frame ごとの可変な引数計画と scratch は共有せず、完了後の読み取り専用世代だけを lease で保持する。

### 初期化、完了と予算

Record は residency を公開しない。初回の同じ frame 内では、同じ command buffer に先行 producer が実際に成功して記録された後だけ、その初期化結果を alias が使える。別の未完了 frame へは未初期化資源を共有しない。

成功した Submit の実 submission を AttachSubmission し、GPU 完了と command の破棄を確認して Retire したときだけ、後続 frame が再利用できる。待機キャンセルと受理不明の Submit は完了とみなさず、既存の確認済み回収まで隔離する。未記録は Dispose、記録済みで確実に未送信の場合だけ command 破棄後に AbortUnsubmitted を使う。最後の lease の回収に失敗した場合、既存 owner の再試行経路を維持する。

一つの renderer の resource coordinator と retained cache が所有権を管理する。共有 native owner は重複計上せず、各 lease と frame metadata を追加計上する。MaxCachedBytes は idle 保持の予算であり、GPU 全体の VRAM 上限ではない。active と隔離中の資源は予算達成のために破棄しない。MaxWorkingBytes は保持経路の capture、変換、compiled owner、staging と page 確保の保守的な上限であり、cache hit も frame 全体の計上から除外しない。

### 同じ親 Graph への接続

AppendResident2D は既存 PreparedDrawingFrame2D と imported target を親 Graph の一つの pass へ接続する。frame と資源の所有権は caller に残し、Graph の破棄自体では residency を公開しない。target と sampled binding を検証してから pass を追加する。transient target と late-bound allocation は追加前に拒否する。

plan.Record、Finish、Submit、AttachSubmission、実完了確認、command と plan の破棄、frame.Retire の順序を使う。初期化と raster を同じ pass に置き、producer だけを cull しない。nested Graph、新しい Canvas、editable GPU scene tree は導入しない。

### 公開 API 差分

基準コミットからの追加は以下である。既存 IRenderer2D、3 種類の PrepareAsync、5・6 引数 constructor と全命令別 interface を維持する。

~~~diff
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
