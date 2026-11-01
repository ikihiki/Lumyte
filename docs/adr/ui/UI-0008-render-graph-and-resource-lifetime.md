# ADR-UI-0008: 親RenderGraphと既存Graphics資源世代へのUI接続

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 7、前提: PR 3・6

## 背景

UIを3D描画と同じ親RenderGraphへ載せ、ResourceManagerのpipeline設定とdevice復旧を再利用する。[UI-0004](UI-0004-drawing-and-clipping.md)の記録処理はDrawingCanvas2Dを使うため、UI本体にgraphやGPUの所有権を追加する必要はない。

## 決定

### 実装範囲と接続API

UI本体への公開API追加はない。`samples/Lumyte.UI.Sample`へ`--render-graph`と`--resources`の独立した実行経路を追加し、Browserにも利用可能な経路を接続する。NuGetとしてUI.RenderGraphや専用のUI resource providerは作らない。既存のDrawing2D.RenderGraphとResources.Graphics.Drawing2D系パッケージを利用側が参照する。

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。利用する既存APIは[GRAPHICS-0022](../graphics/GRAPHICS-0022-2d-render-graph-layers.md)と[GRAPHICS-0018](../graphics/GRAPHICS-0018-2d-graphics-engine.md)が仕様元である。実装PRで追加するサンプル内部の接続境界を示す。

```diff
+namespace Lumyte.UI.SampleSupport
+{
+    internal sealed class UiGraphFrame : IDisposable
+    {
+        // 全引数借用。target/bindingsは同じ親graphに属すること。
+        // 成功時だけframe ownerを返し、失敗時はこの呼び出しの資源を返す。
+        internal static ValueTask<UiGraphFrame> AppendAsync(
+            RenderGraph graph, RenderGraphDrawList2D list, RenderTargetBinding2D target,
+            DrawingGraphBindings2D bindings, DrawingCommandRenderers2D renderers,
+            ResourceReference<Renderer2DResource>? reference,
+            CancellationToken cancellationToken = default);
+        // rendererとPreparedDrawingPasses2Dを所有。graph/plan/submissionは所有しない。
+        // Waitしない。GPU使用が終わってから呼ぶ。cleanup失敗時は再試行可能。
+        public void Dispose();
+    }
+}
```

上記型名のRenderGraphは`Lumyte.Graphics.RenderGraph.RenderGraph`、描画型とResources型は既存namespaceの型である。UiGraphFrameは新しい描画抽象ではなく、サンプルの保持漏れを防ぐownerであり、直接描画のUiSurface.Recordに必須とはしない。

### 記録・準備・実行

1. RenderGraphCanvas2DをBeginし、[UI-0007](UI-0007-window-input-and-dpi-integration.md)のホスト変換とsurface.Recordを記録してEndする。
2. 呼び出し元の親graphへtargetを作成または一度だけimportし、DrawingGraphBindings2Dを同じgraphに結び付ける。
3. referenceなしなら通常のRenderGraphRenderer2D、ありならreference.CreateRenderGraphRendererでrendererを作る。
4. 同じ親graph.Append2Dでpreparedを得てUiGraphFrameが保持する。UI用の内側graphやcompiled planを作らない。
5. 呼び出し元が他のpass、出力保持・Exportを指定し、Compile→Record→Finish→Submit→完了確認を行う。

layoutと記録には[UI-0003](UI-0003-layout-and-invalidation.md)・UI-0004の契約を使う。ホストがSetViewportで与えた論理サイズから確定したSize/Boundsを直接描画とgraph描画で共有し、中間textureやtargetの寸法で再計測・縮小しない。graph側のclipや画像化もこの幾何へ適用する。

初期UIはgroup opacityやbackdropを生成しない。ホストがUI全体をlayerへ包む場合はBeginLayer/EndLayerをsurface.Recordの外で行い、効果後のclipはGRAPHICS-0022へ従う。個々の子へalphaを分配してgroup opacityを模倣しない。

targetはDrawing2Dが対応する単一mip/layerの形式を使う。Clear/Loadと複数surfaceの背景消去は[UI-0004の全体再描画方針](UI-0004-drawing-and-clipping.md#全体再描画とホストの責務)に従い、背景を作るpassとUI passのtarget accessを親graphへ宣言する。

Loadする背景はdisplay-linear premultipliedの色契約を満たす必要がある。SceneLinear/HDRの3D出力は利用側のtone mapping等を先に行い、異なるsemanticを同じtargetへ暗黙合成しない。

外部textureは親allocation単位で一度だけimportし、共有する同じhandleをbindingsへ渡す。入口state/scope、sampleとattachmentの競合、部分更新のColorRead/ColorWrite、出力Exportを既存RenderGraph契約に従って宣言する。順序依存だけをresource accessの代用にしない。DPIはCanvas行列だけで適用し、RenderTargetBinding2D.PixelScale=1とする。

### 所有権・失敗

| 所有者 | 所有・保持するもの |
| --- | --- |
| アプリのframe coordinator | 外部target、import元、command、submission、GPU完了の確認 |
| 親graph / compiled plan | graph内部の一時資源、生成view、graph lifetimeから移譲した資源 |
| UiGraphFrame | RenderGraphRenderer2DとPreparedDrawingPasses2D |
| 既存rendererの内部token | 一つの元ResourceLeaseまたは直接渡したresource owner |
| UIツリー | CPUの値と所有関係。GPU完了の待機やlease取得はしない |

成功時の終了順はGPU完了→command破棄→plan破棄→UiGraphFrame破棄→Reference返却→DI scope等の所有者破棄とする。UiGraphFrameはpreparedを先に、rendererを後に解放する。DrawListが借用する外部viewやカスタムWidgetの資源ownerもGPU完了まで別途保持する。

Appendが失敗・取消したら新規rendererを返し、変更途中の親graph全体を呼び出し元が破棄する。先行成功frame ownerは勝手に返さない。Compile失敗はGPU記録前なのでownerを返せる。Record失敗では未送信commandを先に破棄し、同じpreparedを再Recordしない。Submit後のWait取消は完了扱いにせず、coordinatorがownerを退役キューへ移す。再試行は新しいframeとして記録・準備する。

### Resourcesとdevice世代

Resourcesなしの経路も残す。使用する場合はManager構築前に既存のAddGraphics2D・公開resource key・汎用pipeline/sampler定義を設定する。UI専用shaderの自動生成、別pipeline cache、batchごとのlease再取得は行わない。

AcquireRenderer2DAsyncはframeの準備前に行い、CreateRenderGraphRendererは既存契約どおり一つの世代leaseを取得してrendererへ移譲する。複数資源のpublicationを揃える場合は既存ResourceSnapshotを使う。HotReload後の新frameは新rendererを作り、既存in-flight frameの参照を差し替えない。

deviceロストでは新規Prepare/Submitを止め、既存Graphicsのcoordinatorで旧deviceの退役を確認してから旧frameを返す。復旧とResourcesのRebuildRuntimeAsync後にtarget/import/bindings/rendererを再生成する。UiSurfaceとCPUツリー、入力の意味状態は維持できるが、復旧中に入力を受けるかはホスト方針として明示する。復旧を行うホストは操作を止め、FocusLostで押下を取り消す。標準サンプルは毎frameの完了を取消なしで待つ検証用途とし、自動復旧coordinatorは含めない。復旧手順は既存Resourcesの統合fixtureで確認する。

旧GPU viewを含まない標準Widgetのツリーは再利用できる。GPU viewを記録するカスタムWidgetは利用側が新世代参照へ更新し、新DrawListを記録する。失効検査で例外が出た際に埋め込みshaderや旧資源へfallbackしない。

## 検討した代替案

UI専用の内側RenderGraphは親graphの同期と資源管理を分断する。専用ResourceManagerやpipeline providerは既存世代管理と重複する。UIは既存Canvasへ記録し、利用側のframe ownerだけを追加する。

## 結果と影響

同じUIを直接描画と親graphの両方で使い、Resourcesの有無も独立して選べる。一方、frame ownerの保持期間とGPU失敗後の退役をアプリが管理する必要がある。pool、差分cache、blur等のコントロール固有効果は別途設計する。

## 検証方針

- 同じUIの直接描画と親graphを同形式・同DPI・同Load条件で画素比較し、UI前後のpass順を確認する。
- 単独UIのClear経路と同frameの3D背景をLoadする経路で、連続frameの半透明合成が蓄積せず、Widget移動・削除後に背景が復元することを確認する。
- Append/Compile/Recordの失敗、取消、Wait取消でownerが早期解放されず、二重解放されないことをfixtureで確認する。
- 既存Resourcesテスト基盤でpipeline設定の反映、元leaseの共有、HotReload後の新frame、旧frameの保持を確認する。
- device世代fixtureで新target/bindings/rendererへの切替とCPUツリーの維持を確認し、実GPUで再描画する。実deviceロストを試せない環境はfixture検証と区別する。
- Vulkan/wgpuとBrowserで利用可能な経路を実行し、未検証の経路をREADMEへ明示する。

## 実装と検証記録

接続補助は非配布の`samples/Lumyte.UI.Shared`へ置く。Nativeのoffscreen実行は`--render-graph vulkan|wgpu`と`--resources vulkan|wgpu`、Browserの提示は`?render-graph`と`?resources`で選択する。ResourcesモードはManager生成前に公開pipeline keyを上書きし、設定したpipelineが選ばれたことを確認する。

`PreparedDrawingPasses2D.Dispose`は成功したentryを除去し、解放失敗したentryを保持して次のDisposeで再試行できるよう修正する。UI公開APIの変更はない。

2026-10-29にVulkan／wgpuのソフトウェアGPUで、同じtargetを3 frame再利用した直接描画との厳密画素比較、非等方DPI、Clear/Load、移動・削除後の背景復元を確認した。GPU実行と世代fixtureの範囲、Browserの環境制約は[実行サンプル](../../../samples/Lumyte.UI.Sample/README.md)と[Browser README](../../../samples/Lumyte.UI.Browser.Sample/README.md)へ記録する。deviceロスト後の実GPU再描画は未検証で、世代切替のfixtureと区別する。

## Element基盤との接続

2026-10-31、#83はElementから記録済みの描画命令を受け取り、従来のticket・lease・退役の
境界を維持する。Elementや現在のWidgetをGPU実行時に読み直さない。
統合35件とDrawing2D 101件が成功し、準備失敗、依存順の清掃、途中失敗後の資源保持を確認した。
Resources.Graphics 48件、Browser sampleのbuild、Native sampleのrecord-only実行も成功した。
