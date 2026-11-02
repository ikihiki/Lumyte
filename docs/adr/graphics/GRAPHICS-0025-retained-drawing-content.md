# ADR-GRAPHICS-0025: 不変の描画内容と配置構造を保持する

- 状態: 提案
- 日付: 2026-11-03
- 比較元: main `fbc8105`

## 背景

現行のDrawingCanvas2D.Replayは参照追加ではなく全命令の再記録であり、DrawList2Dの固定化とRenderer2Dの準備も全体を走査する。
PR #105のコピー削減だけでは、変更していない描画内容を毎frame再構築する問題は解消しない。
UIは既にElement identity、部分木更新、layout cacheを持つため、別のUI treeやdirty推論engineを追加せず描画側へ接続したい。

本ADRは[部分更新設計](../../design/drawing2d-incremental-rendering.md)の主要判断を記録する提案であり、製品の保持型描画は未実装である。
本PRの設計・benchmark-only試作・測定根拠を、製品アーキテクチャの導入完了とは扱わない。

## 決定（提案）

**不変content、配置/property/orderの構造、device別compiled asset、frameごとのone-shot実行ownerを分離して保持する。**
差分更新と[bulk構築（GRAPHICS-0026）](GRAPHICS-0026-bulk-drawing-updates.md)は同じ描画内容とordered plannerへ合流する。

### 内容・配置・identity

- fragmentは独立に再記録できる不変payload。配置instanceのnode IDとpayload identityを分け、同じpayloadの複数配置を許す。
- scene IDと世代付きnode IDで別scene・削除済み・再利用前のIDを拒否する。配列indexを永続identityにしない。
- content、properties、structure/order、外部依存のrevisionを分け、単一dirty boolで全cacheを捨てない。
- self描画とchild/slot参照を交互に置けるordered recipeを保持し、透明描画を型・paint順に全体sortしない。
- fragment内clipの捕捉座標と外側scope clipを区別する。子transform変更で親clipを移動しない。
- 論理fragmentとGPU page/runを分離し、1 Elementを1 drawへ固定しない。

### 不変公開とCPU所有権

single-writer transactionが変更leafと経路だけをCOWし、検証済みrootをCommitでatomicに公開する。
失敗・取消・未CommitのDisposeでは旧rootを維持し、取得済みsnapshotは後続編集・削除後も読める。
Captureはroot lease取得に留め、全命令のReplay、flatten、全体hashをしない。no-op Commitは同root・同content revisionとする。
旧snapshotのIDは捕捉版で解決する。Capture/Commit/Dispose間のlease取得競合を同期で防ぐ。

初版CPU配列はGC所有でよい。Canvas専有bufferはdetachできるが、外部mutable入力は防御的コピーまたは明示的移譲を要する。
read-only wrapperだけでbacking bufferの不変性を保証せず、reader寿命が未定義の公開配列をpoolへ返さない。
巨大fragment内の1命令変更はfragment全体の置換になり得る。完全なcommand粒度更新を保証しない。

### 三つの寿命とdevice epoch

1. CPU snapshotは記録と外部依存参照を保持する。
2. compiled assetはgeometry・paint解析・GPU payload等をdevice/runtime epoch、shader/binding/resource世代別に保持する。
3. fresh frame ownerはtarget/binding/uploadとasset leaseを持ち、Recordを一回だけ行う。

消費済みPreparedDraw2D、command buffer、前frameのtransient RenderGraph handleをcacheしない。
CPU配列でもargument table固有の物理参照を含めばdevice依存である。symbolic解析とbinding materializeを分離する。
geometry、paint、clip座標、DPI/tolerance、資源世代等の実依存をcache keyへ含める。
借用texture/viewはCPU強参照だけでDisposeを防げず、snapshot全体がdevice非依存になるわけではない。
callerは将来のsnapshot使用と全GPU使用まで借用資源を生存させ、device loss後は再resolveまたは新viewで置換する。
Resources連携では既存publication/leaseを用い、基本Drawing2DにResourceManager依存を追加しない。

in-flight pageは上書きせずCOWし、最後のframe使用まで旧pageを保持する。uploadのRecordをresident公開と誤認しない。
Submit成功・完了、未送信、成否不明を区別し、Wait取消では退役しない。未送信commandは先に破棄してleaseを返す。
cache evictionは参照を外すだけで使用中資源を即解放しない。予算、frame上限、backpressureを設ける。
custom rendererは明示opt-in/revision契約がなければ毎frame準備する。

### UI・実行制御との協調

既存Element identityと確定layout結果をscene occurrenceへ対応づけ、offsetのみは配置、size依存描画はcontentを更新する。
RecordSelf/RecordChildの順序を捕捉し、custom OnRecordは再利用契約か明示invalidateがなければvolatileとする。
UiVisualLayer/navigationも共通adapterへ接続し、必要なraster layer、共有要素の除外・後段合成を維持する。
animation、時刻、presentation receipt、遷移完了通知は既存update/presentation境界で進め、再記録の有無に結び付けない。
layout、scheduler、surface能力照会、submission待機は別責務であり、本方式がそれらの費用を消すとはしない。

### 公開API候補（未実装）

比較元main `fbc8105`に以下のAPIは存在しない。名称・型・公開範囲は候補で、詳細契約は設計文書を参照する。

```diff
+namespace Lumyte.Graphics.Drawing2D
+{
+    // scene由来の世代付きID。default/stale/別sceneは拒否。
+    public readonly struct DrawingNodeId2D;
+    public sealed class DrawingScene2D : IDisposable
+    {
+        public DrawingScene2D();
+        public DrawingNodeId2D Root { get; }
+        public DrawingSceneUpdate2D BeginUpdate(); // single-writer
+        public DrawingSnapshot2D Capture(); // 全命令の連結なし
+        public void Dispose(); // 新規取得を閉じ、既存snapshotは破壊しない
+    }
+    public sealed class DrawingSceneUpdate2D : IDisposable
+    {
+        public DrawingNodeId2D AddGroup(DrawingNodeId2D parent, int index);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawList2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawList2D content);
+        public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawingContent2D content);
+        public void ReplaceContent(DrawingNodeId2D node, DrawingContent2D content);
+        public void SetTransform(DrawingNodeId2D node, System.Numerics.Matrix3x2 transform);
+        public void SetClip(DrawingNodeId2D node, Path2D? clip, FillRule2D rule = FillRule2D.NonZero);
+        public void Move(DrawingNodeId2D node, DrawingNodeId2D parent, int index);
+        public void Remove(DrawingNodeId2D node);
+        public DrawingSnapshot2D Commit(); // 検証後atomic公開。失敗時は旧root
+        public void Dispose(); // 未Commitは取消
+    }
+    public sealed class DrawingSnapshot2D : IDisposable
+    {
+        public ulong Revision { get; }
+        public void Dispose();
+    }
+    public sealed class Renderer2D
+    {
+        // 既存rendererへの追加。毎回freshなone-shot owner。
+        public ValueTask<PreparedBulkDraw2D> PrepareAsync(DrawingSnapshot2D snapshot,
+            RenderTarget2D target, DrawingBulkRenderers2D renderers,
+            CancellationToken cancellationToken = default);
+    }
+}
```

DrawingContent2D、DrawingBulkRenderers2DとPreparedBulkDraw2DはGRAPHICS-0026の候補型を共用する。snapshot経路も明示consumerとcompletion-aware ownerを使い、既存DrawList向けPreparedDraw2Dのone-shot契約を変更しない。既存custom rendererは明示legacy adapterを経由し、resident版は関連submissionの完了とcommand破棄を確認してから公開する。

## 検討した代替案

- buffer detachだけ: 正当な局所改善だが毎frameの全記録・全準備が残る。
- rootを全再記録してからdiff: 比較前の構築費用を払い、UIの既存変更通知を活用できない。
- flat mutable配列の上書き: 旧snapshot/in-flight使用を破壊し、挿入・reorderで全体移動が起きる。
- 外部scene engineへの置換: Chromium property tree、Flutter addRetained、Velloを設計資料として参照したが、既存backend/coverage/拡張契約の置換は別評価を要する。

## 結果と影響

静止・局所更新の再記録/再準備を減らせる可能性がある一方、tree/COW、cache管理、旧版保持、GPU退役の複雑さと常駐容量が増える。
親property更新は部分木再解決を要し得る。command record/drawは可視packet数に比例し得るため、frame全体O(dirty)を約束しない。

## 検証方針

[CPU試作](../../performance/drawing2d-incremental-comparison.md)と[実GPU平坦化比較](../../performance/drawing2d-hardware-comparison.md)では互換Replay/Endの費用が残った。
[direct試作](../../performance/drawing2d-direct-pages.md)はbenchmark-onlyで、固定順・固定数・単色矩形・identity transform・clipなし・固定1024 targetに限定する。
局所更新は改善したが全更新は退化し、一般path/text/UIやdevice復旧の成立証明ではない。
[正式12 workload](../../performance/formal-rendering-workload-comparison.md)はmain/#105のみの比較であり、保持UI全般の比較ではない。

実装時は全再記録との画素比較、混在/半透明/clip/reorder、同一payload複数配置、旧snapshot、stale ID、失敗原子性を検証する。
3 frame in flight、slotの中間revision追従、未送信upload、Wait取消、device loss/hot reload、予算超過とcustom非opt-inも対象とする。
Build/Commit/Capture/compile/pack/upload/encode/submitを分け、割当・保持byte・draw数・退役待ちを測る。未実装部分の性能倍率は未確定である。

## 互換性・非目標・別途決定する事項

既存Canvas/DrawList/Renderer経路とone-shot契約を維持し、利用者の移行を必須にしない。#105/#106の製品最適化・基盤変更を取り込む判断ではない。
初期版は全targetをClearして全描画してよい。dirty rectangle、partial present、別UI engine、独自資源管理系は非目標。
custom再利用interface、RenderGraph layer/effect編集、page粒度/cache予算、UI opt-in範囲は別reviewで決める。
