# ADR-UI-COMPONENTS-0006: Separatorによる操作を持たない区切り線

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 独立した追加
- 変更元: [UI-0013の表示・装飾・進捗](../ui/UI-0013-component-catalog-and-delivery-plan.md#2-表示装飾進捗-8種類)の未実装Separator候補を具体化する

## 背景

画面作成者は、項目や操作群を見た目で区切るときに、入力を受け取らない線を配置したい。基準コードのRectangleでも単色の細い矩形は描けるが、方向と線の厚さを表す契約がなく、親制約に合わせるために幅・高さを用途ごとに記述する必要がある。

根拠は[UI-0013](../ui/UI-0013-component-catalog-and-delivery-plan.md)、[UI-0003の制約と確定サイズ](../ui/UI-0003-layout-and-invalidation.md#制約と確定サイズ)、[UI-0004](../ui/UI-0004-drawing-and-clipping.md)、基準コード `src/UI/Lumyte.UI/Compose.Rectangle.cs` と `src/UI/Lumyte.UI/RectangleElement.cs` とする。カテゴリui-componentsは個別部品の利用・配置・意味情報の契約、uiカテゴリは共通ツリーと実行基盤の契約を扱う。

## 決定

Lumyte.UIへSeparatorを追加し、Orientation、Thickness、Colorから有限の本体寸法と中央の区切り線を決める。独自のfocus・クリック・callbackを持たせず、既存のlayout、描画snapshot、withによる修飾、共通の可視性へ参加させる。

### 利用者の変更前・変更後

変更前の次の記述は、幅100・高さ1の固定した白い矩形として区切りを表す。

```csharp
using Lumyte.UI;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

var line = Rectangle(width: 100, height: 1, fill: new Color2D(1, 1, 1, 1));
```

変更後は区切り線という意図を保持し、同じ固定寸法を明示できる。widthを省略すれば、boundedな横軸の利用可能幅を使う。

```csharp
using Lumyte.UI;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

var line = Separator(width: 100, orientation: UiOrientation.Horizontal,
    thickness: 1, color: new Color2D(1, 1, 1, 1));
```

後者は追加APIの設計であり、基準コードでは使えない。表示にはColorを明示する。既定の透明色は自動でテーマやサービスから補わない。

### 制約と描画

既定OrientationはHorizontal、Thicknessは1、Colorは透明とする。Horizontalでは長軸が幅、短軸が高さ、Verticalでは長軸が高さ、短軸が幅になる。Thicknessはfinite・非負とし、不正値は宣言の採用前にArgumentOutOfRangeExceptionで拒否する。未定義のOrientationも同例外で拒否し、Colorは既存Color2Dの0〜1・finite検証に従う。

共通の修飾列を外側から内側へ処理し、最内側で既存Width/Heightを親制約内へclampしてtightにした本体制約を受け取る。長軸の希望寸法はmaxがfiniteならmax、unboundedならmin、短軸はThicknessとする。それらを本体制約へclampして一度で本体寸法を確定する。

線は長軸全体へ描き、短軸の中央に置く。実線幅はThicknessと実際の短軸寸法の小さい方とし、描画が本体矩形から外へはみ出さない。Thickness=0または本体のいずれかの軸が0なら線を記録しないが、親のminや明示寸法によって占有領域は残り得る。

例としてHorizontalで幅max=100、高さ0〜20、Thickness=1なら100×1、幅max=Infinityかつ幅min=0なら0×1となる。高さtight=4なら本体は高さ4で線は中央の厚さ1、厚さ10で高さmax=3なら本体と線幅は3になる。NaNやInfinityを最終寸法として公開しない。

DPIの適用は既存hostで一度だけ行う。logical単位のThicknessを独自に物理pixelへ丸めず、位置・色・線のsnapshotを既存Drawing2D経路へ記録する。外側の修飾枠はSeparatorの線とは別の描画として扱い、canvasの状態を後続要素へ漏らさない。

### 意味情報と入力

既定RoleはNone、Focusableはfalseとし、Click/Edit capability、専用command、OnClick等のcallbackは追加しない。共通のAutomation snapshotに存在しても、roleを指定しただけで操作可能にならない。OSアクセシビリティbridgeやseparator専用roleの追加は今回決めない。

共通のIsEnabled、IsVisible、HitTestVisible、ClipToBoundsの契約は継続する。pointerが届いた場合もUnhandledを返し、captureやfocusを自発的に取得しない。既存の共通入力経路以上の背面要素へのclick-through保証は追加しない。利用者が共通Focusable等を明示変更した場合の挙動は基盤契約に従い、部品専用のactivationは生まれない。

### 更新と所有権

Widgetは値と宣言のみを持ち、Elementが幾何・描画cacheを保持する。child slot、購読、timer、独自GPU資源を追加しない。同じ型/key/親/slotなら再構築でElementとAutomation IDを維持する。共有宣言から別Surfaceを作っても幾何は共有しない。

OrientationまたはThicknessの変更はlayout・入力幾何・描画を、Colorだけの変更は描画を無効化する。Width/Heightと可視性、修飾列の変更は既存の共通差分判定へ合流する。値引数には既存UiArgumentのSignal/Computed/UiAnimated接続を使い、constructorやgetterから購読を開始しない。

準備中の検証失敗は旧treeを維持する。公開後のlayoutや記録の失敗は既存の停止・清掃規則へ従い、既に公開した幾何の全面rollbackは保証しない。

### 公開APIの差分

基準実装にSeparatorは存在しない。UI-0013の未実装候補を次の主要宣言として具体化する。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            [Composable]
+            public partial class Separator : Widget
+            {
+                public Separator();
+                // 既定Horizontal。未定義値は採用前に拒否する。
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                // 既定1。finite・非負。0なら線を描かない。
+                [ComposeParameter] public float Thickness { get; set; }
+                // 既定透明。値の検証はColor2Dの契約に従う。
+                [ComposeParameter] public Color2D Color { get; set; }
+                protected override UiElement CreateElement();
+            }
         }
     }
 }
```

Color2DはLumyte.Graphics.Drawing2Dの既存型である。通常の生成factoryを使い、メソッドチェーン、childインデクサ、返却Widgetの差し替えは追加しない。

## 検討した代替案

- Rectangleの寸法だけで表す: 固定寸法には足りるが、区切りの方向と親に追従する長さを用途ごとに補う必要がある。
- Border修飾を空内容へ付ける: 囲み枠と一方向の区切り線では寸法・描画の意味が異なるため同一部品にしない。
- テーマから既定色を暗黙取得する: 短く書けるが、UI本体へテーマやservice locatorへの依存を作るため採用しない。

## 結果と影響

線の方向・厚さ・色を配置意図として表し、bounded軸では親の利用可能寸法へ追従できる。unbounded長軸では有限minを使うため、見える長さが必要なら親制約またはWidth/Heightを明示する。

### 受け入れ条件

- 別assemblyからfactoryとOrientation/Thickness/Colorの値引数adapterを利用できる。
- 両向きでtight/loose/unbounded、明示寸法、0寸法、Thickness=0、親より大きいThicknessを検証する。
- 上記の100×1、0×1、高さ4中の厚さ1、高さ3へのclampを数値と画素で確認する。
- 色のみの変更でlayout回数が増えず、方向と厚さの変更では必要なlayoutを実行する。
- 修飾による本体原点、ClipToBounds、非一様DPI、描画snapshotの寿命とcanvas状態を確認する。
- 既定でfocus/capture/clickを取得せず、role偽装でもAutomation Clickが拒否される。
- Signal更新、準備失敗、除去・再構築で余分な購読やcallback、資源所有が発生しない。

本文は設計であり、実装・検証完了を表すものではない。
