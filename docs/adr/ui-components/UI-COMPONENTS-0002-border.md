# ADR-UI-COMPONENTS-0002: 同じWidgetへ順序付き内側枠を設定するBorder

- 状態: 提案
- 日付: 2026-11-05
- 基準コミット: `8d2b1a7f30f09cc086da5bb422740289eee7a9d5`
- 変更関係: 一部変更
- 変更元: [UI-0013](../ui/UI-0013-component-catalog-and-delivery-plan.md)「2. 表示・装飾・進捗: 8種類」「配置とComposition」
- 変更対象: 未実装候補BorderのWidget継承と0〜1 childを、同じWidgetへの枠操作へ変更する
- 継続する判断: 他の部品の責務・依存方向、WidgetとElementの分離、一方向layout、通常入力とAutomationの共通経路

## 背景

画面の作成者は既存のButtonへ枠を付けたいが、基準コードには標準の枠指定がない。UI-0013のBorderは未実装の単一child decorator候補であり、そのままでは枠のために宣言と表示の階層が増える。

基準SHAの `src/UI/Lumyte.UI/Compose.Button.cs` と `src/UI/Lumyte.UI/Compose.Rectangle.cs` は既存部品を定義し、`src/UI/Lumyte.UI/UiElement.Layout.cs` は寸法と子の位置を分離する。[UI-0003「制約と確定サイズ」](../ui/UI-0003-layout-and-invalidation.md#制約と確定サイズ)と[UI-0004「記録手順と座標」](../ui/UI-0004-drawing-and-clipping.md#記録手順と座標)を基礎とする。

## 決定

BorderをWidgetではないComposable操作定義とし、withから同じWidgetの修飾列へ色と線幅を追加する。Elementが枠幅を内側の制約から差し引き、その層の矩形の内側へ線を描くため、具体的な返却型と子slotを保って枠を付けられる。

### 利用者の変更前・変更後

本体120×32のButtonへ白い幅2の枠を付ける。変更前の実在APIだけでは枠と内容のinsetを表せず、独自Elementによるlayoutと描画が必要になる。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32)[Rectangle(width: 16, height: 16)];
```

変更後は今回の提案APIを追加する。Surfaceへの接続とRectangleの色は省略している。

```csharp
using Lumyte.UI;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

var save = Button(width: 120, height: 32, with: [
    Border.Stroke(new Color2D(1, 1, 1, 1), 2)
])[Rectangle(width: 16, height: 16)];
```

十分なloose制約なら外寸124×36、本体120×32、本体原点(2,2)となる。子インデクサとButtonの名前付きslotは元のButtonへ接続され、factoryは同じButtonを返す。

### 順序・所有権・更新

[COMPOSITION-0001](../composition/COMPOSITION-0001-declarative-composition.md)「構築・with・子要素の順序」のAction適用順と例外伝播を維持する。操作は値を捕捉し、適用時に検証して不変の設定を追加する。同種も上書きせず、先頭が最も外側となる。一般の設定操作の意味は変えない。

たとえばPadding.All(12)の後にStrokeを置けば余白の内側に枠を描き、逆なら枠の内側に余白を置く。外部の可変list、任意実行delegate、Element参照は保持しない。失敗項目は追加せず、以前のActionの副作用は巻き戻さない。操作定義は単独表示できず、ラッパー、返却Widgetの置換、チェーンを導入しない。

mounted宣言の直接変更では更新せず、新しい宣言を採用する。再構築で列を一から作り、旧列へ再適用して蓄積しない。型・key・親・slotが同じならElement、focus、押下状態、Automation IDを保持する。層は独自key、子、購読、mount hookを持たない。

寸法・層の矩形・cacheはSurfaceごとのElementへ置き、共有宣言間で共有しない。PrepareUpdateの公開前検証失敗は旧宣言・入力状態・購読・cacheを保持し、採用後は旧列のcacheを使わない。Signal等は構築関数で読み直し、新しい宣言を返す。直接のSignal/Computed/UiAnimated引数や専用scheduler、timerは追加しない。

### 制約・描画・入力

幅wを四辺のinsetとし、各軸の内側min/maxは `max(0, min-2*w)` と `max(0, max-2*w)` とする。無限maxは無限のまま保つ。全修飾を通してから既存Width/Heightを親制約内へclampしてtight化し、LayoutOverrideと実際の子のlayoutを各一回行う。戻りは内側寸法に2*wを加え、その層の親制約へclampする。

領域が線幅より小さくてもinsetを比例縮小せず、本体原点(w,w)を保ち、本体を0まで縮める。線は利用可能な枠矩形内へ収め、太い線でも外へ広げない。幅0はinsetも描画もなし。角丸、背景fill、影は提供しない。枠の記録と本体・子の記録はcanvas状態を分離する。

有限値同士の和・差・幅の加算がoverflowすれば不正な幾何としてlayoutを失敗させ、Infinity/NaNを公開しない。失敗は基準コードの停止規則に従い、全幾何のrollbackは保証しない。IsVisible=false等のlayout除外は修飾全体へ適用する。

Size/Boundsは親が扱う外側寸法・配置、ContentBoundsは外側原点から見た本体矩形、ContentSizeは本体寸法とする。子offset、RecordOverride/OnRecord、OnInput/OnProbePointerDownは本体座標を使う。独自Elementの自己描画・inside判定はContentSizeへ移し、親は子のSizeを使い続ける。無修飾では既存結果を保つ。

暗黙のclipは追加しない。ClipToBounds=trueなら外側Boundsで描画と入力をclipし、子のclipも維持する。枠は新たなpointer targetでも元のButtonのクリック領域でもなく、capture済みpointerは本体外にも既存規則で配送する。PointerPosition/IsInsideとprobeには同じ本体座標を使い、InputのSurface座標は維持する。

Automation nodeのBoundsは外側配置で、枠nodeや新たなClick capabilityを作らない。Clickは本体の操作可能領域と既存probeが一致する点を選び、本体0面積・clip外・遮蔽時は通常の操作不能結果とする。IME caret・選択・Popup anchor・focus表示も同じ本体からSurfaceへの変換を使い、祖先の本体offsetとhost境界のDPI変換を各一度だけ適用する。

列の追加・削除・並べ替え・線幅変更は共通CompareConfiguration経路でlayout・入力幾何・描画を無効化する。枠色だけなら描画のみを無効化し、layout、不要なhostサービス検証、IME文書変更を起こさない。描画は既存snapshotとGPU資源寿命に従い、GPU処理が後からElementの幾何を読まない。

### API差分

以下は基準コードへの追加である。WidgetはCompose.Definitions.Widget、Color2DはLumyte.Graphics.Drawing2D、属性はLumyte.Compositionを使用する。生成入口とprivate定義を区別し、Core CompositionへUI/Graphics依存を追加しない。

```diff
 namespace Lumyte.UI
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            [Composable]
+            public partial class Border
+            {
+                public Border();
+                // 必須値。widthはfinite・非負。不正値は適用時にArgumentOutOfRangeException。
+                // 0はinsetも描画もなし。colorは既存Color2D規則。所有権・失敗は本文に従う。
+                [ComposeAction] private static void Stroke(Widget target, Color2D color, float width);
+            }
         }
     }
 }
```

生成拡張クラス内の主要宣言は次の追加となる。Actionの反変性でButton等のwithへ渡せる。

```diff
 namespace Lumyte.UI
 {
+    public static class ComposeBorderCompositionExtensions
+    {
+        public static Action<Compose.Definitions.Widget> Stroke(this Compose.Definitions.BorderFactory factory, Color2D color, float width);
+    }
 }
```

旧候補の比較元は基準時点のUI-0013「2. 表示・装飾・進捗: 8種類」であり、実装済みAPIの削除ではない。

```diff
-public partial class Border : Widget { public Border(); }
+public partial class Border { public Border(); } // Composable操作定義。独自childなし。
```

## 検討した代替案

- 独立decorator: 所有権が明白だが、枠だけのためにWidgetとElementの階層が増える。
- 単一Borderプロパティ: 短いが、枠の反復や余白との内外関係を順序で表せない。
- Widget置換やチェーン: 既存コンテナを使えるが、具体的な戻り値と子slotの接続契約を変える。

## 結果と影響

利用者は部品を変えずに枠を追加でき、色だけの更新でlayoutを避けられる。層の計算と保存は必要で、性能改善は未測定である。ui-componentsは個別部品の公開利用・layout契約、uiは共通基盤の判断を扱う。任意の修飾プラグイン、テーマ、OSアクセシビリティbridgeは対象外とする。

### 受け入れ条件

- 外部assemblyから明示usingでStrokeをwithへ渡せ、Buttonの戻り値・通常内容・Background slotが変わらない。
- loose上限200×100で本体120×32に幅2なら外寸124×36、原点(2,2)。tight 3×2で幅4なら本体0×0、原点(4,4)、外寸3×2となり、枠画素は3×2の内側だけに収まる。
- 本体20×10へ幅2と幅3を順に追加すると外寸30×20、原点(5,5)。幅0は寸法・画素を変えず、NaN/Infinity/負値は拒否し、有限線幅の加算overflowは失敗する。
- 本体20×10、余白12、枠幅2では外寸48×38が同じでも、余白→枠の枠原点は(12,12)、枠→余白では(0,0)となる。両順序と反復で各層の矩形を確認する。
- 一回layout、空内容、unbounded、幅依存高さ、無修飾互換、再構築の非蓄積、複数Surfaceのcache分離、PrepareUpdate/layout失敗の違いを確認する。
- 枠・本体・clip・captureでpointerとAutomationの可否・callback回数が一致し、keyboard、非一様DPI、IME、Popupの座標、色のみ更新の無効化範囲を確認する。

本文は設計提案であり、実装済みまたは検証済みを意味しない。
