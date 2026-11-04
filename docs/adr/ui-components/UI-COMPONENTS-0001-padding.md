# ADR-UI-COMPONENTS-0001: 同じWidgetへ順序付き余白を設定するPadding

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 一部変更
- 変更元: [UI-0013](../ui/UI-0013-component-catalog-and-delivery-plan.md)「1. レイアウト・スクロール: 14種類」「配置とComposition」
- 変更対象: 未実装候補PaddingのWidget継承と0〜1 childを、同じWidgetへの余白操作へ変更する
- 継続する判断: 他の部品の責務・依存方向、WidgetとElementの分離、一方向layout、通常入力とAutomationの共通経路

## 背景

画面の作成者は既存のButtonへ余白を付けたいが、基準コードには標準の余白指定がない。UI-0013のPaddingは未実装の単一childコンテナ候補であり、そのままでは余白のために宣言と表示の階層が増える。

基準SHAの `src/UI/Lumyte.UI/Compose.Button.cs` と `src/UI/Lumyte.UI/Compose.Rectangle.cs` は既存部品を定義し、`src/UI/Lumyte.UI/UiElement.Layout.cs` は寸法と子の位置を分離する。[UI-0003「制約と確定サイズ」](../ui/UI-0003-layout-and-invalidation.md#制約と確定サイズ)の親制約優先と一回layoutを維持する。

## 決定

PaddingをWidgetではないComposable操作定義とし、既存のwithから同じWidgetの修飾列へ四辺の値を追加する。Elementが列を外側から解釈し、具体的な返却型と子slotを保ったまま余白を宣言できる。

### 利用者の変更前・変更後

本体120×32の周囲へ12の余白を付けたい場合を示す。変更前は次の実在APIだけでは余白を表せず、独自Elementによる配置が必要になる。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32)[Rectangle(width: 16, height: 16)];
```

変更後は今回の提案APIを追加する。Surfaceへの接続とRectangleの色は省略している。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32, with: [Padding.All(12)])[
    Rectangle(width: 16, height: 16)
];
```

十分なloose制約なら外寸144×56、本体120×32、本体原点(12,12)となる。子インデクサとButtonの名前付きslotは元のButtonへ接続され、factoryは同じButtonを返す。

### 順序・所有権・更新

[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)「構築・with・子要素の順序」のAction適用順と例外伝播を維持する。操作は値を捕捉し、適用時に検証して不変の設定を列へ追加する。同種も上書きせず、先頭の修飾が最も外側となる。一般の設定操作の意味は変えない。

外部の可変list、任意実行delegate、Element参照を設定として保持しない。失敗項目は追加せず、以前のActionの副作用は巻き戻さない。設定用インスタンスはWidgetではなく単独表示できず、ラッパー、返却Widgetの置換、メソッドチェーンを導入しない。

mounted宣言の直接変更では更新せず、新しい宣言を採用する。再構築では列を一から作り、旧列へ再適用して蓄積しない。型・key・親・slotが同じならElement、focus、押下状態、Automation IDを保持する。層は独自key、子、購読、mount hookを持たない。

寸法・層の矩形・cacheはSurfaceごとのElementへ置き、共有宣言間で共有しない。PrepareUpdateの公開前検証失敗は旧宣言・入力状態・購読・cacheを保持し、採用後は旧列のcacheを使わない。Signal等は構築関数で読み直し、新しい宣言を返す。直接のSignal/Computed/UiAnimated引数や専用scheduler、timerは追加しない。

### 制約・幾何・入力

各軸の余白合計をsとすると、内側のmin/maxは `max(0, min-s)` と `max(0, max-s)` とし、無限maxは無限のまま保つ。全修飾を通してから既存Width/Heightを親制約内へclampしてtight化し、LayoutOverrideと実際の子のlayoutを各一回行う。戻りは内側寸法にsを加え、その層の親制約へclampする。

余白が領域より大きくても比例縮小せず、左・上の指定量を本体位置として保ち、本体を0まで縮める。有限値同士の和・差のoverflowは不正な幾何としてlayoutを失敗させ、Infinity/NaNを公開しない。失敗は基準コードの停止規則に従い、全幾何のrollbackは保証しない。IsVisible=false等のlayout除外は修飾全体へ適用する。

Size/Boundsは親が扱う外側寸法・配置、ContentBoundsは外側原点から見た本体矩形、ContentSizeは本体寸法とする。子offset、RecordOverride/OnRecord、OnInput/OnProbePointerDownは本体座標を使う。独自Elementの自己描画・inside判定はContentSizeへ移し、親は子のSizeを使い続ける。無修飾なら本体原点0で既存結果を保つ。

暗黙のclipは追加しない。ClipToBounds=trueなら外側Boundsで描画と入力をclipし、子のclipも維持する。余白は新たなpointer targetでも元のButtonのクリック領域でもなく、capture済みpointerは本体外にも既存規則で配送する。PointerPosition/IsInsideとprobeには同じ本体座標を使い、InputのSurface座標は維持する。

Automation nodeのBoundsは外側配置で、修飾nodeを作らない。Clickは本体の操作可能領域と既存probeが一致する点を選び、本体0面積・clip外・遮蔽時は通常の操作不能結果とする。IME caret・選択・Popup anchor・focus表示も同じ本体からSurfaceへの変換を使い、祖先の本体offsetとhost境界のDPI変換を各一度だけ適用する。

列の追加・削除・並べ替え・余白変更は共通CompareConfiguration経路でlayout・入力幾何・描画を無効化する。描画は既存snapshotとGPU資源寿命に従い、GPU処理が後からElementの幾何を読む契約は追加しない。

### API差分

以下は基準コードへの追加である。WidgetはCompose.Definitions.Widget、属性はLumyte.Compositionを使用する。生成入口とprivate定義を区別し、Core CompositionへUI依存を追加しない。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            [Composable]
+            public partial class Padding
+            {
+                public Padding();
+                // 必須値。finite・非負。不正値は適用時にArgumentOutOfRangeException。
+                [ComposeAction] private static void All(Widget target, float value);
+                // 四辺とも同じ検証。順序・所有権・失敗は本文の契約に従う。
+                [ComposeAction] private static void Sides(Widget target, float left, float top, float right, float bottom);
+            }
         }
     }
 }
```

生成拡張クラス内の主要宣言は次の追加となる。Actionの反変性でButton等のwithへ渡せる。

```diff
 namespace Lumyte.UI
 {
+    public static class ComposePaddingCompositionExtensions
+    {
+        public static Action<Compose.Definitions.Widget> All(this Compose.Definitions.PaddingFactory factory, float value);
+        public static Action<Compose.Definitions.Widget> Sides(this Compose.Definitions.PaddingFactory factory, float left, float top, float right, float bottom);
+    }
 }
```

旧候補の比較元は基準時点のUI-0013「1. レイアウト・スクロール: 14種類」であり、実装済みAPIの削除ではない。

```diff
-public partial class Padding : Widget { public Padding(); }
+public partial class Padding { public Padding(); } // Composable操作定義。独自childなし。
```

## 検討した代替案

- 独立コンテナ: 所有権が明白だが、余白のためにWidgetとElementの階層を増やす。
- 単一Paddingプロパティ: 短いが、繰り返しと他の修飾との順序を表せない。
- Widget置換やチェーン: 入れ子を減らせるが、具体的な戻り値と子slotの接続契約を変える。

## 結果と影響

利用者は部品を変えずに余白を追加できる。各層の計算と保存は必要で、性能改善は保証しない。ui-componentsは個別部品の公開利用・layout契約、uiは共通基盤の判断を扱う。任意の利用者定義修飾やOSアクセシビリティbridgeは対象外とする。

### 受け入れ条件

- 外部assemblyから明示usingでAll/Sidesをwithへ渡せ、Buttonの戻り値・通常内容・Background slotが変わらない。
- loose上限200×100で本体120×32にAll(12)を付けると144×56。tight 10×8では本体0×0、原点(12,12)、外寸10×8となり、clipなしでは原点が外へ出ることを許す。
- 本体20×10へAll(2), All(3)なら30×20、原点(5,5)。Sides(1,2,3,4)なら24×16、原点(1,2)。0、空内容、unbounded、幅依存高さ、overflow、NaN/Infinity/負値も確認する。
- 一回layout、無修飾互換、再構築の非蓄積、複数Surfaceのcache分離、PrepareUpdate失敗とlayout失敗の差を確認する。
- 余白・本体・clip・captureの境界でpointerとAutomationの可否・callback回数が一致し、keyboard、非一様DPI、IME、Popupの座標を確認する。

本文は設計提案であり、実装済みまたは検証済みを意味しない。
