# ADR-GRAPHICS-0015: 共通command API上のレンダーグラフ

- 状態: 提案
- 日付: 2026-10-11

## 背景

[CommandBuffer](GRAPHICS-0007-command-buffers-and-submission.md) により、Wgpu／Vulkan／Browser は共通の copy、render／compute pass、barrier を記録できる。複数 pass で resource を共有すると、実行順と scope、texture layout の追跡を利用者が重複実装する必要がある。

レンダーグラフ追加の依頼に対し、既存の明示的な submit と GPU 完了待機を維持する初期設計を提案する。本 ADR の詳細な対象範囲はまだ合意されていないため、状態は提案とする。作業ツリーの実装とテストはこの案のレビュー材料であり、採用済みと扱わない。

## 決定

### 責務と初期範囲

独立した `Lumyte.Graphics.RenderGraph` が `Lumyte.Graphics.Abstractions` のみに依存する。backend の低水準 contract を変更せず、外部所有 texture／型付き unmanaged buffer の全 allocation を登録し、callback pass の resource 宣言から依存関係と barrier を生成する。

resource hazard の向きは pass の追加順で決める。read→write、write→read、write→write と texture state の変更に edge を作り、明示的な DependsOn を併合する。実行可能な pass は追加順で選び、循環を Compile 時に拒否する。read-only texture の state 変更も順序を必要とする。同じ state の read は scope を合併し、後続 write の barrier の Before に全 reader を含める。

write／state 変更から連続 read へ移る barrier の After には、次の write／state 変更までの全 reader scope を含める。最初の reader の stage だけに可視化すると、異なる stage の後続 reader を同期できないためである。

pass はアクセスごとに state と BarrierScope を宣言し、Access flags から read／write を推定する。backend と同じ stage／access、usage／state の制約を登録時に検証する。同一 allocation の alias import を拒否し、pass 内の重複アクセスは一つの合併 scope で宣言する。Undefined texture に対する初回 read／read-write は Compile 時に拒否する。部分 write の初期化範囲や StoreOp.Discard の内容追跡は行わない。

### 公開API

比較元は origin/main（`4b8902f5226c17ce86c337d09434a3ae4043bf2d`）。対象 namespace の公開 API はすべて追加する。

```diff
+namespace Lumyte.Graphics.RenderGraph
+{
+    public sealed class RenderGraph
+    {
+        public RenderGraph();
+        // 非所有、全allocation。duplicate allocationはArgumentException。
+        // initialState=Undefinedは内容破棄。既存状態とpreceding scopeは呼び出し側が指定する。
+        public RenderGraphTexture ImportTexture(IGraphicsTexture texture, TextureState initialState, BarrierScope initialScope);
+        // 読み出す既存bufferの内容初期化は利用者の責任。
+        public RenderGraphBuffer<T> ImportBuffer<T>(IGraphicsBuffer<T> buffer, BarrierScope initialScope) where T : unmanaged;
+        // callbackはcopy／render／computeを記録し、encoderを閉じて返る。
+        public RenderGraphPass AddPass(string name, Action<RenderGraphContext> record);
+        // GPU workなし。循環／undefined readはInvalidOperationException。
+        public CompiledRenderGraph Compile();
+    }
+    // handleはgraph identityを保持し、利用者は生成／継承しない。
+    public abstract class RenderGraphResource { }
+    public sealed class RenderGraphTexture : RenderGraphResource { }
+    public sealed class RenderGraphBuffer<T> : RenderGraphResource where T : unmanaged { }
+    public sealed class RenderGraphPass
+    {
+        public string Name { get; }
+        // 非空access、定義済みstate。別graph／duplicate use／不整合flagsはArgumentException。
+        public RenderGraphPass UseTexture(RenderGraphTexture texture, TextureState state, BarrierScope scope);
+        public RenderGraphPass UseBuffer<T>(RenderGraphBuffer<T> buffer, BarrierScope scope) where T : unmanaged;
+        // 同graphの別pass。後から追加したpassへの依存も可能。
+        public RenderGraphPass DependsOn(RenderGraphPass pass);
+    }
+    public sealed class RenderGraphContext
+    {
+        public IGraphicsCommandBuffer Commands { get; }
+        // 当該passが宣言したhandleだけを解決。未宣言はArgumentException。
+        // callback終了後のcontext利用はInvalidOperationException。
+        public IGraphicsTexture GetTexture(RenderGraphTexture texture);
+        public IGraphicsBuffer<T> GetBuffer<T>(RenderGraphBuffer<T> buffer) where T : unmanaged;
+    }
+    public sealed class CompiledRenderGraph
+    {
+        public IReadOnlyList<string> PassNames { get; }
+        // 正常実行後の予定値。未使用resourceはinitial値を返す。GPU queryは行わない。
+        public TextureState GetFinalTextureState(RenderGraphTexture texture);
+        public BarrierScope GetFinalScope(RenderGraphResource resource);
+        // one-shot。Recording command bufferかつactive encoderがないこと。
+        // Finish／Submit／Wait／Disposeを呼ばない。callback失敗でもplanを消費する。
+        public void Record(IGraphicsCommandBuffer commands);
+    }
+}
```

### 所有権と失敗

graph は resource／view／command／submission を所有しない。利用者が同じ device の allocation と commands を渡し、必要な GPU 完了まで寿命を維持する。全 mip／layer／buffer 要素を barrier 範囲とする。Compile は宣言を snapshot するが callback の capture 値は利用者が管理する。

Record の callback が失敗すると途中の recording は破棄する。Finish、submit、完了待機は呼び出し側が行う。compiled plan は一回だけ使い、正常実行後の final state／scope を次の graph に渡す。内部同期を追加せず、builder／plan の並列操作と GPU 同期は利用者が保証する。

### 検証方針

native binding 不要の test project を solution に追加し、既存 CI の solution tests で実行する。fake command buffer の記録から barrier の state、scope、range、callback 順序を検証する。RAW／WAR／WAW と layout 変更による循環、read scope の合併、同 state write、undefined read、snapshot、foreign handle、alias、one-shot と callback 失敗を確認する。実 GPU／Browser での統合検証は別途必要であり、unit tests の成功から GPU 動作保証を推定しない。

## 検討した代替案

- backend ごとに graph を作る: 共通 command API があるため、依存解決を重複実装する必要がない。
- shader binding を解析してアクセスを推測する: copy／attachment／indirect と副作用を網羅できないため、明示宣言を初期契約にする。
- 初回から transient allocation／pool／alias／culling を含める: submission をまたぐ allocation 寿命と出力 resource の契約が増えるため、登録済み resource の依存と同期を先に提供する。
- pass の versioned output により追加順に依存しない DAG を作る: 複数 write と部分更新の version 管理が必要になるため、初期版では既存 command の逐次的な resource 利用を追加順で表す。

## 結果と影響

共通 API 上で pass と barrier の接続を一箇所に記述でき、native backend に依存しない検証が可能になる。既存 command の利用者には破壊的変更を加えない。

allocation 全体の同期は保守的で、独立した mip／layer／byte 範囲の並列性は活用しない。実際の命令アクセスと宣言の一致、attachment discard／部分初期化の扱いは callback の責任になる。自動 allocation、shader data buffer、subresource 追跡、pass culling、複数 queue と性能最適化は後続で設計する。
