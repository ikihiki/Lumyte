# ADR-UI-0004: 既存CanvasへのUI描画記録と矩形クリップ

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 3、前提: PR 2

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。描画hookと実行時の子参照はElementへ移す。Canvas・DrawList・GPUの契約は維持する。
過去の実装記録とElement移行後の検証記録を分けて記載する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

[UI-0003](UI-0003-layout-and-invalidation.md)で確定した配置を既存Drawing2Dへ描画する。UI内部にrendererやGPU資源管理を作らず、直接描画と後続の親RenderGraph接続で同じ記録処理を使いたい。

## 決定

### 公開APIと実装範囲

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。DrawingCanvas2DとColor2DはGraphics.Drawing2Dの既存型を使う。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiSurface
+    {
+        // 必要なlayoutを先に確定。Begin済みCanvasを借用し、状態を復元する。
+        // nullはArgumentNullException。Begin前はInvalidOperationException。
+        public void Record(DrawingCanvas2D canvas);
+    }
+    public partial class UiElement
+    {
+        protected virtual void OnRecord(DrawingCanvas2D canvas);
+        protected virtual void RecordOverride(DrawingCanvas2D canvas);
+        protected void RecordSelf(DrawingCanvas2D canvas);
+        protected void RecordChild(UiElement child, DrawingCanvas2D canvas);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 既定false。自身と全ての子へ同じローカル矩形clipを適用。
+                [ComposeParameter] public bool ClipToBounds { get; set; }
+            }
+            [Composable]
+            public partial class Rectangle : Widget
+            {
+                public Rectangle();
+                // 既定transparent。全成分finiteかつ[0,1]、straight sRGB。
+                // Color2Dの生成・initで検査。UIのsetterは色を検証しない。
+                [ComposeParameter] public Color2D Fill { get; set; }
+            }
+        }
+    }
+}
```

RectangleとButton背景のlayoutは[UI-0003の組み込みコンテナー規則](UI-0003-layout-and-invalidation.md#組み込みコンテナー)に従う。Fill変更はレイアウトを無効化しない。PanelElement / StackElement / ButtonElementのOnRecordは空で、Buttonの見た目は背景スロットで明示する。IsPressedに応じた見た目の自動変更はテーマの後続設計で扱う。

### 全体再描画とホストの責務

初期は毎Recordで全可視要素の命令を再生成し、描画dirty、DrawList cache、InvalidateVisual APIは追加しない。

命令の再生成と、描画先の前frameの画素を消すことは別の責務である。初期サンプルは描画するframeごとに描画先全体を再構成する。単独UIではホストがtargetをClearし、必要な背景とUIを描く。3D等へ重ねる場合は、そのframeで再構成した背景をLoadしてUIを合成する。前frameのUIを含む画素へ半透明UIを重ね続けず、移動・非表示にした要素の跡も背景から消す。Hostedを含む同じtargetのClearはホストが最初に一度だけ行い、surface.RecordはClearしない。damage領域だけの再描画・保持buffer・部分Presentは初期範囲に含めず、導入時は別途測定と契約を設ける。

### 記録手順と座標

RecordはUI-0003で確定したSize/Boundsを使い、targetやclipの残り領域に合わせた子の縮小・再layoutを行わない。Stackのoverflowもサイズを保ち、clipで表示領域を制限する。

1. 生存を検査し、外側SaveでCanvasがBegin済みであることをlayout前に確認する。必要なUpdateLayoutを実行する。
2. surfaceローカル原点のviewport矩形をClipRectする。viewportのいずれかの軸が0なら描画を省略する。
3. 可視ノードについてSave、BoundsのX/YによるTranslate、必要なら原点0・最終サイズのClipRectを行う。
4. コンポーネントのRecordOverrideを実行する。既定実装はRecordSelfの後、登録順に全コレクションの子を記録する。Buttonは背景→RecordSelf→通常内容を自身のoverrideで指定する。基盤はBackground等の名前を解釈しない。RecordSelfはOnRecordを独立したSave/Restoreで囲み、カスタム描画の変換・clipが子や兄弟へ漏れないようにする。
5. 各Saveに対応するRestoreをfinallyで実行し、外側のCanvas状態を復元する。

Rectangleは原点0、幅・高さがBoundsのサイズのFillRectを一命令記録する。サイズ0やalpha0なら省略してよい。半透明の命令を色や型で全体ソートしない。IsEnabledとHitTestVisibleは描画を抑制せず、IsVisibleだけが部分木を省略する。

UIの位置は論理pixelであり、DPIのscaleを記録しない。ホストが付けたCanvas変換の内側にElementの原点を合成する。clipの追加時点の変換を保存する既存Canvas契約に従い、親clipは子のTranslateで移動しない。clipなしの親の外へ子が描かれることは許可するが、surfaceのviewportからは出さない。

### 所有権と失敗

RecordはCanvasのBegin/End、renderer生成、GPU Prepare/Submit/Wait/Presentを実行しない。成功したDrawListはUIツリーを参照せず、surfaceの変更・Dispose後も既存Drawing2Dのsnapshot契約に従って使用できる。カスタム描画が借用したGPU viewの所有者は別途GPU完了まで保持する。

CPUツリーとRecordは作成threadへ固定しない。接続中も操作が重ならなければ別threadで記録できる。同時アクセスの同期は呼び出し側が管理し、phaseと同期的再入の検査は行わず、hostが描画対象の確定と処理順序を管理する。CanvasやGPU backend固有の制約はそれぞれの契約に従う。

描画中は確定した宣言を変更せず、同じsurfaceの処理を重ねない。setterやRecordにphase検査は追加しない。例外時はCanvas状態の復元を試みるが成功済みの命令は残るため、呼び出し元はframe全体をCancelして再記録する。不正なカスタムSave/RestoreやCancel呼び出しまで隔離する保証は設けない。

### サンプルと検証の配置

`samples/Lumyte.UI.Sample`に、CompositionでPanel/Stack/RectangleとButton背景を組み立てる最小offscreenサンプルを置く。入力はまだ接続せず、viewport変更による再配置と通常Canvas2D→Renderer2Dの経路、PPMへのreadbackを示す。`record-only`はGPUなしで記録まで実行する。renderer、target、command、submissionの所有権は[GRAPHICS-0018](../graphics/GRAPHICS-0018-2d-graphics-engine.md)に従う。

命令snapshotの統合テストは、既存の内部snapshot検査ができるDrawing2D.TestsからUIを参照して配置する。製品の公開APIや依存方向、friend assemblyを検査のために拡張しない。実GPUの検証は既存Graphics.Testsへ追加する。

## 検討した代替案

UI専用の描画リストとrendererを作ると既存Canvasのclip・paint・GPU寿命管理を重複する。最初からRenderGraph専用にすると直接描画にもgraph依存を要求するため、DrawingCanvas2Dへ記録する。

## 結果と影響

一つのUI記録処理を複数の描画経路で再利用できる。記録は全可視ノードと命令数に比例し、描画cacheや差分更新による性能改善は別途測定して判断する。

### コンポーネントによる描画順の拡張

Elementは`protected virtual void RecordOverride(DrawingCanvas2D canvas)`、`protected void RecordSelf(DrawingCanvas2D canvas)`、`protected void RecordChild(UiElement child, DrawingCanvas2D canvas)`を公開する。RecordOverrideは任意の登録コレクションを任意の順序で記録でき、所有権・layoutの走査順とは独立である。例えばEmbeddeds→自身→Foregroundの順をコンポーネントで指定する。RecordSelfはOnRecordを呼び、RecordChildは直接の子をローカル座標・clip付きで再帰記録する。null childはArgumentNullException、他の親の子・未所有の子はArgumentExceptionとする。

コンポーネントは表示する各子の記録と順序を担当する。RecordOverride自身のcanvas操作はscope内で釣り合わせ、RecordSelfを使う限りOnRecordの変換は後続の子へ漏れない。例外時はsurfaceがノード境界のcanvas状態を復元する。描画対象の選択はlayoutで全可視子の処理を必要とする規則とは別である。

## 検証方針

- 累積位置、背景→自身→内容、半透明順序、0寸法、非表示、外側Canvas状態の保持を命令snapshotで確認する。
- 親子clip、親外の子、viewport clip、ホストのscale・Translateとの合成を確認する。
- Stackの高さ100に高さ80の子を二つ置き、二つ目のサイズ80を保ったまま、ClipToBoundsで描画領域だけが変わることを確認する。
- callbackの例外、通常の値設定、失敗後のCancelと次frame再試行を確認する。
- 単独UIの連続frameで半透明要素が濃くならず、移動・非表示後に旧領域が背景へ戻ることを確認する。複数Hosted surfaceの記録で先行surfaceをClearしないことも確認する。
- 既存Graphics GPUテストへ小さな固定UIを追加し、Vulkan/wgpuで基準画素と照合する。座標・順序のCPUテストと役割を分ける。

## 実装・検証記録

2026-10-29、#77の所有ツリーと#78のlayoutを前提に、Rectangle・Canvas記録・clip・Color2Dの値検証・サンプルを#79へまとめた。WidgetChildren、Composition生成、基盤とlayoutのテストは先行PRに含め、本PRで置換しない。

コンポーネント固有の描画順、直接の子だけを記録できる規則、OnRecordの状態隔離を含め、Drawing2D 99件のReleaseテストが成功した。Vulkan/wgpuのGPU画素テスト2件もLavapipe上で成功し、Buttonの背景描画順とframeごとの再構築を確認した。

## Element基盤への移行記録

2026-10-31、#79の記録・子描画・clipをElementへ移し、RectangleElementとButtonElementが
現在のWidget設定と自身の幾何から描画する構成にした。Widgetに描画hookを残さない。
UI 102件とDrawing2D 100件が成功した。同一実体の設定交換後も過去の描画命令が
変わらないこと、custom slotの描画順・Canvas状態の復元・失敗時の取消を検証した。
record-onlyサンプルは2種類のviewportで実行が成功した。
