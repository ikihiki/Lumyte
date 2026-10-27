# ADR-UI-0004: 既存CanvasへのUI描画記録と矩形クリップ

- 状態: 提案
- 日付: 2026-10-27
- 対応: 実装PR 3、前提: PR 2

## 背景

[UI-0003](UI-0003-layout-and-invalidation.md)で確定した配置を既存Drawing2Dへ描画する。UI内部にrendererやGPU資源管理を作らず、直接描画と後続の親RenderGraph接続で同じ記録処理を使いたい。

描画が読むBoundsは、一方向layoutで確定したSizeと親のoffsetから作る。Recordは子をtargetやclipの残り領域へ縮小・再layoutしない。特にStackのoverflowは確定サイズを保ち、clipだけで表示領域を制限する。

## 決定

### 公開APIと実装範囲

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。DrawingCanvas2DとColor2DはGraphics.Drawing2Dの既存型を使う。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiSurface
+    {
+        // 必要なlayoutを先に確定。Begin済みCanvasを借用し、状態を復元する。
+        // nullはArgumentNullException。Begin前・再入はInvalidOperationException。
+        public void Record(DrawingCanvas2D canvas);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 既定false。自身と全ての子へ同じローカル矩形clipを適用。
+                [ComposeParameter] public bool ClipToBounds { get; set; }
+                // 原点・clip適用済み。Begin/End/Cancelと不均衡なSave/Restoreを禁止。
+                // 基底実装は何も記録しない。子の走査はsurfaceが行う。
+                protected virtual void OnRecord(DrawingCanvas2D canvas);
+            }
+            [Composable]
+            public partial class Rectangle : Widget
+            {
+                public Rectangle();
+                // 既定transparent。全成分finiteかつ[0,1]、straight sRGB。
+                // setterで検査。不正値はArgumentOutOfRangeException。
+                [ComposeParameter] public Color2D Fill { get; set; }
+            }
+        }
+    }
+}
```

Fill変更はレイアウトを無効化しない。RectangleのLayoutOverrideはUI-0003の規則どおりboundedな軸ではmax、unboundedな軸では0を選びConstrainする。Buttonの背景はButtonの確定サイズのtight制約でlayoutされる。初期は毎Recordで命令を作るので、描画dirty、DrawList cache、InvalidateVisual APIは追加しない。Panel/Stack/ButtonのOnRecordは空で、Buttonの見た目は背景スロットで明示する。IsPressedに応じた見た目の自動変更はテーマの後続設計で扱う。

### 記録手順と座標

1. IdleでUpdateLayoutを実行し、成功後にRecord phaseへ移る。
2. 外側Saveを行い、surfaceローカル原点のviewport矩形をClipRectする。viewportが0なら描画を省略する。
3. 可視ノードについてSave、BoundsのX/YによるTranslate、必要なら原点0・最終サイズのClipRectを行う。
4. 背景スロット、OnRecord、通常内容を順に記録する。各列の順序を維持する。
5. 各Saveに対応するRestoreをfinallyで実行し、最後にIdleへ復帰する。

Rectangleは原点0、幅・高さがBoundsのサイズのFillRectを一命令記録する。サイズ0やalpha0なら省略してよい。半透明の命令を色や型で全体ソートしない。IsEnabledとHitTestVisibleは描画を抑制せず、IsVisibleだけが部分木を省略する。

UIの位置は論理pixelであり、DPIのscaleを記録しない。ホストが付けたCanvas変換の内側にWidgetの原点を合成する。clipの追加時点の変換を保存する既存Canvas契約に従い、親clipは子のTranslateで移動しない。clipなしの親の外へ子が描かれることは許可するが、surfaceのviewportからは出さない。

### 所有権と失敗

RecordはCanvasのBegin/End、renderer生成、GPU Prepare/Submit/Wait/Presentを実行しない。成功したDrawListはUIツリーを参照せず、surfaceの変更・Dispose後も既存Drawing2Dのsnapshot契約に従って使用できる。カスタム描画が借用したGPU viewの所有者は別途GPU完了まで保持する。

OnRecord中のプロパティ・子変更、Record/Dispatch/UpdateLayoutの再入は拒否する。例外時はCanvas状態の復元を試みるが成功済みの命令は残るため、呼び出し元はframe全体をCancelして再記録する。不正なカスタムSave/RestoreやCancel呼び出しまで隔離する保証は設けない。

### サンプルと検証の配置

`samples/Lumyte.UI.Sample`に、CompositionでPanel/Stack/RectangleとButton背景を組み立てる最小サンプルを追加する。入力はまだ接続せず、viewport変更による再配置と通常Canvas2D→Renderer2Dの経路を示す。renderer、target、command、submissionの所有権は[GRAPHICS-0018](../graphics/GRAPHICS-0018-2d-graphics-engine.md)に従う。

## 検討した代替案

UI専用の描画リストとrendererを作ると既存Canvasのclip・paint・GPU寿命管理を重複する。最初からRenderGraph専用にすると直接描画にもgraph依存を要求するため、DrawingCanvas2Dへ記録する。

## 結果と影響

一つのUI記録処理を複数の描画経路で再利用できる。記録は全可視ノードと命令数に比例し、描画cacheや差分更新による性能改善は別途測定して判断する。

## 検証方針

- `DrawingRecordingTests`: 累積位置、背景→自身→内容、半透明順序、0寸法、非表示、外側Canvas状態の保持を命令snapshotで確認する。
- `ClipRecordingTests`: 親子clip、親外の子、viewport clip、ホストのscale・Translateとの合成を確認する。
- Stackの高さ100に高さ80の子を二つ置き、二つ目のサイズ80を保ったまま、ClipToBoundsで描画領域だけが変わることを確認する。
- `RecordingFailureTests`: callbackの例外、変更・再入の拒否、失敗後のCancelと次frame再試行を確認する。
- 既存Graphics GPUテストへ小さな固定UIを追加し、Vulkan/wgpuで基準画素と照合する。座標・順序のCPUテストと役割を分ける。
