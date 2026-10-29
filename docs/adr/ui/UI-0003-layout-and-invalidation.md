# ADR-UI-0003: 制約を下へ渡す一方向レイアウト

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 2、前提: PR 1
- 更新: 2026-10-28

## 背景

[UI-0002](UI-0002-widget-tree-and-composition.md)の保持型ツリーをviewportへ配置する。広い制約で希望サイズを求め、後から別サイズへ配置する方式では、幅に応じて高さが変わるText等の内容を再計算する必要がある。サイズが確定する時点と、親が決められる範囲を明確にしたい。

Flutterのbox layoutを参考に、親が制約を渡し、子がその制約内の確定サイズを返し、親が子の位置を決める方式を採用する。Compositionによる構築を使用し、構築関数の再実行と表示要素の対応付けは[UI-0002](UI-0002-widget-tree-and-composition.md#構築関数の再実行による動的な子の変更)に従う。再構築とlayoutは別段階とし、layout・入力・描画で共通の表示要素のツリーを使う。現在の静的ツリーの実装に対する宣言と表示状態の分離は、reconcilerの実装で整理する。

## 決定

### 制約と確定サイズ

一つのレイアウト呼び出しは`Layout(node, constraints) → Size`である。制約は各軸のmin/maxを持ち、visibleな子は制約内の有限サイズを返す。親は返されたサイズを後から縮めたり伸ばしたりせず、位置だけを指定する。将来のTextは渡された幅制約で文字組みし、その結果の高さを返す。

minはfiniteかつ非負、maxはmin以上のfinite値またはPositiveInfinityとする。NaN、負値、NegativeInfinity、min>maxを拒否する。min=maxをtight、min=0をlooseと呼ぶ。unboundedは最大値が無限であるという制約であり、無限サイズを返す許可ではない。数値演算でInfinity-InfinityやNaNを作らない。

Width/Heightのnullはその軸へ追加の制約を与えない。指定値はfiniteかつ非負で、親制約内へclampしてその軸をtightにしてからLayoutOverrideへ渡す。親のtight制約が優先するため、例えば幅800のtight制約下のWidth=160は幅800になる。小さい内容を置く場合はPanel等で子へloose制約を渡す。

### 公開API

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。Vector2はSystem.Numerics、Rect2DはGraphics.Drawing2Dを使う。

```diff
+namespace Lumyte.UI
+{
+    // 不変。defaultは全成分0の有効なtight制約。
+    public readonly record struct UiBoxConstraints
+    {
+        // minはfiniteかつ非負。maxはmin以上、PositiveInfinityを許可。
+        // 不正値はArgumentOutOfRangeException。
+        public UiBoxConstraints(float minWidth, float maxWidth,
+            float minHeight, float maxHeight);
+        public float MinWidth { get; }
+        public float MaxWidth { get; }
+        public float MinHeight { get; }
+        public float MaxHeight { get; }
+        public bool HasBoundedWidth { get; }
+        public bool HasBoundedHeight { get; }
+        public bool IsTight { get; }
+        // sizeはfiniteかつ非負。不正値はArgumentOutOfRangeException。
+        public static UiBoxConstraints Tight(Vector2 size);
+        // minを0にする。maximumのPositiveInfinityを許可する。
+        public static UiBoxConstraints Loose(Vector2 maximum);
+        // 最大値を保ちminだけを0にする。
+        public UiBoxConstraints Loosen();
+        // 指定軸を親制約内へclampしてtightにする。null軸は維持。
+        // 明示値のInfinity/NaN/負値はArgumentOutOfRangeException。
+        public UiBoxConstraints Tighten(float? width = null, float? height = null);
+        // finiteかつ非負の候補を制約内へclampする。返値もfinite。
+        public Vector2 Constrain(Vector2 size);
+    }
+    public enum UiOrientation { Vertical, Horizontal }
+    public sealed class UiSurface
+    {
+        // 既定(0,0)。finiteかつ非負。rootにはTight(logicalSize)を渡す。
+        public void SetViewport(Vector2 logicalSize);
+        // dirtyならLayoutを実行し全結果を公開。cleanなら何もしない。
+        public void UpdateLayout();
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 最後に公開した確定サイズ。getterはlayoutを実行しない。
+                public Vector2 Size { get; }
+                // 親が指定したoffsetとSizeから作る親ローカル矩形。
+                public Rect2D Bounds { get; }
+                // 既定null。指定値はfiniteかつ非負。
+                [ComposeParameter] public float? Width { get; set; }
+                [ComposeParameter] public float? Height { get; set; }
+                // 既定true。falseの部分木はlayout対象外、公開値を0にする。
+                [ComposeParameter] public bool IsVisible { get; set; }
+                protected void InvalidateLayout();
+                // 指定寸法を適用済みの制約。制約内のfiniteサイズを返す。
+                // 基底の葉実装はconstraints.Constrain(Vector2.Zero)。
+                protected virtual Vector2 LayoutOverride(UiBoxConstraints constraints);
+                // 現在のlayout中に直接の子を一度だけlayoutする。
+                // 当該passの確定サイズを返す。公開Sizeは前passのまま。
+                protected Vector2 LayoutChild(Widget child, UiBoxConstraints constraints);
+                // LayoutChild後に直接の子の位置だけを一度指定する。
+                // offsetは両成分finite。負の位置も許可する。サイズは変更しない。
+                protected void SetChildOffset(Widget child, Vector2 offset);
+            }
+            [Composable]
+            public partial class Stack : Panel
+            {
+                public Stack();
+                // 既定Vertical。不正enumはArgumentOutOfRangeException。
+                [ComposeParameter] public UiOrientation Orientation { get; set; }
+                // 既定0。finiteかつ非負。可視の子同士の間だけに置く。
+                [ComposeParameter] public float Spacing { get; set; }
+            }
+        }
+    }
+}
```

Rect2Dを公開するためLumyte.UIから既存Lumyte.Graphics.Drawing2Dへの参照を追加する。独自の矩形型やlayout専用の第二の所有treeは作らない。Stackの生成インデクサーはPanelの同じ引数形を明示的に隠してStack自身を返し、継承した値引数と内容を通常Composeから利用する。必要なGeneratorの修正は既存の型付き生成契約の不具合修正として同じPRへ含める。

### 一回の処理とカスタムコンテナー

内部の非virtualなLayout入口が指定寸法、phase、返値、子の処理漏れを検証し、利用側はLayoutOverrideだけを拡張する。カスタムコンテナーはPanelを継承してChildrenを所有し、次の順で処理する。

1. 子ごとの制約を決め、LayoutChildを呼んでサイズをローカル変数へ保存する。
2. 制約内で自身のサイズを計算する。
3. 保存した子サイズと自身のサイズから各子のoffsetを求め、SetChildOffsetする。
4. 自身のサイズを返す。呼び出し元の親が自身の位置を指定する。

サイズ決定は通常の再帰走査内で完結し、全ツリーの希望サイズ計算後に別の配置走査は行わない。親が子列を複数回ループして位置を計算することは許可するが、同じ可視子へ二回LayoutChildを呼んで別制約を試すことは初期契約では拒否する。公開Size/Boundsは前passの値なので、現在の子サイズはLayoutChildの返値を使う。

直接の子以外、重複layout、位置の重複指定、layout前の位置指定、可視子の処理漏れ、phase違反はInvalidOperationException。nullはArgumentNullException、非finite offsetはArgumentOutOfRangeException。非表示の子はhelper内部で0として扱いoverrideを呼ばず、親が位置を指定しても公開Boundsは0へ正規化する。

LayoutChild/SetChildOffsetの失敗はpassへ記録し、custom override内で例外を捕捉しても失敗したpassを公開しない。カスタム状態のsetterは、fieldを書き換える前にInvalidateLayoutを呼び、phase違反で途中の値変更を残さない。Size/Boundsのgetterと構築/子列の初回初期化は自動layoutを実行しない。

### 組み込みコンテナー

Panelは各可視子へ`constraints.Loosen()`を渡す。自身はboundedな軸ではmaxを使い、unboundedな軸では子のサイズの最大値を使ってConstrainする。子は原点0へ重ね、確定後のstretchは行わない。葉やコンテナーが空き領域を埋める必要がある場合は、その子へ最初からtight制約を渡すカスタムコンテナーで表現できる。

Stackは主軸を`[0, +Infinity]`、交差軸を`[0, 親max]`として各可視子をlayoutし、主軸のサイズ合計とgapを使って位置を決める。自身のboundedな軸はmax、unboundedな軸は内容の合計または最大値を使い、親制約内へConstrainする。交差軸の配置はstartとし、子を後から最大幅・高さへ伸ばさない。空Stackの内容サイズは0で、gapを加算しない。

例えば高さ100の縦Stackに高さ80の子を二つ置いたら、子はともに80のままで、二つ目はY=80になる。親の高さは100で、残り領域に合わせて二つ目を20へ縮めない。overflowはlayoutエラーではなく、viewportとClipToBoundsによる描画・hit testの制限で扱う。横Stackの非flexなTextへは無限の幅制約が渡るため、折り返したい場合はWidthか別のコンテナーで有限幅を事前に与える必要がある。

Buttonは通常内容へloose制約を渡して一度layoutし、そのサイズ（内容なしなら0）を自身の制約へConstrainする。その確定サイズをTightにして背景の全子をlayoutする。内容と背景のoffsetは0。背景のサイズをButton自身のサイズ計算へ戻さず、内容も後からstretchしない。

Rectangleは後続PRで導入し、boundedな軸はmax、unboundedな軸は0を選んでConstrainする。Width/Heightは先にtight化されるので明示寸法を優先できる。背景としてtight制約を受ければButton全体を埋める。

rootはviewportのtight制約とoffset0を持つ。非表示のrootだけはlayout対象外として公開Size/Boundsを0にする。合算はdoubleで行い、親の有限maxへclampできる自身のサイズはclamp後にfloatへ変換する。unboundedな自身のサイズや子offsetをfiniteなfloatで表せない場合はInvalidOperationExceptionとし、後続の子を潰したりInfinityを公開したりしない。

### 無効化と失敗時の公開状態

寸法、可視性、子の初回初期化、Stack設定、カスタムlayout値の変更でInvalidateLayoutを呼ぶ。自身からrootまでdirtyを伝播し、同値代入では無効化しない。viewport変更もdirtyにする。surfaceへの再接続も新しいviewportで再layoutする。子列は通常内容・背景ごとに一回だけ初期化でき、同じ参照・同じ順序でも再初期化は拒否する。初期実装はdirtyなsurfaceをrootから再layoutし、子サイズへの依存を使ったrelayout boundaryや部分cacheは導入しない。

一回のpassの確定サイズとoffsetを作業tableに保持する。全ノードの成功後、Size/Boundsをまとめて公開しdirtyを解除する。この公開処理は再layoutを行わず、ユーザーコードも呼ばない。overrideの例外、制約違反のサイズ、非finite値、子の処理漏れでは旧公開値を保ちdirtyを維持する。overrideが不正なサイズを返した場合は黙ってclampせずInvalidOperationExceptionとする。

Layout中の値・子構成の変更とUpdateLayout再入を拒否する。幾何を使う通常のDispatchとRecordはIdle中に必要なUpdateLayoutを済ませてから自身のphaseへ移る。pointer Cancel、FocusLost、後続PRのUiKeyCancelInputは幾何を使わず、layoutを経由せず所有状態を解除する。再入・破棄後の検査は取消にも適用する。CPU上のWidget/UiSurfaceは作成threadに固定せず、操作が重ならなければ別threadで順次利用できる。利用者が明示的に行う並行操作の同期は利用者が管理する。失敗したlayoutの作業値を入力や描画へ渡さない。surface接続解除後の公開値は最後の値で、未接続時の配置の有効性は保証しない。固定の子列の取り外し・並べ替えは提供しない。

通常Dispatchのlayout失敗時の系列取消は後続のpointer実装PRで具体化する。UpdateLayout/Recordの失敗でホストが入力配送を止める場合も、Idleへの復帰後にFocusLost等で清掃できる。取消は旧公開値も参照せず、dirtyとlayoutの再試行可能性を保つ。

### 初期対象と拡張境界

one-passはサイズ決定の基本プロトコルであり、任意の制約問題を一回で解く保証ではない。intrinsic/dry layout、兄弟の最大サイズに全員を揃える再計測、一般の循環依存は初期対象外とする。Flex/Expanded、scroll/sliver、baseline、alignmentの追加は別ADRで子への制約と追加計測の可否を定義する。

初期Stackは非flexなRow/Columnに相当する部分だけを扱い、Flutterの全Flex機能を実装したとはしない。将来残り領域へflex子を配分する場合は、非flex子をlayoutしてからflex子へ有限の制約を渡す契約にする。無限の主軸で有限の残余領域を配分できるとはしない。

## 検討した代替案

- Measure/Arrangeを分離すると、配置時に子サイズを変える場合に再計算の規則が必要になるため、サイズ確定を一つの入口へ集約する。
- 制約を最大値だけにすると、固定サイズと任意サイズを明示的に区別しにくいためmin/maxを持たせる。
- 全制約をfiniteに限定すると、Stackの内容サイズや将来のscroll向けのunboundedな軸を表現できない。maxだけのPositiveInfinityを許可し、返値はfiniteに限定する。
- 兄弟の最大値へ合わせるために暗黙の再layoutを行う案は、呼び出し回数とコストが不明確になるため初期には導入しない。

## 結果と影響

制約下で子のサイズが確定するため、文字の折り返し等の幅依存計算と整合する。カスタムコンテナーの入口を一つにでき、Composition・所有権・描画先の依存方向を維持できる。

一方、親のtight制約、unboundedな軸、overflowの理解が必要になる。通常layoutはノード数と子数に対して線形で、作業tableと公開処理のコストを伴う。性能と割当量は実測で評価する。

## 検証方針

- tight/loose、default、Tighten、Constrain、親制約優先、min/max逆転、NaN、各Infinityの許可・拒否を確認する。
- rootのtightサイズ、縦横Stack、空列、gap、非表示、Panelのbounded/unbounded軸、Buttonの内容→背景の順と確定サイズを確認する。
- 幅で高さが変わるfixtureで、実際の幅制約がlayout前に渡り、後から幅や高さを変更しないことを確認する。
- 高さ100に80+80を配置して二つ目のサイズを保つこと、clipの有無で描画とhit testの領域だけが変わることを後続PRと確認する。
- 別assemblyのPanel派生型で継承パラメーター、LayoutChildの返値、offset指定を使い、各可視子が一度だけlayoutされることを確認する。
- resize・子差し替え・再表示の再layout、同値代入、clean passの呼び出し回数を確認する。
- 非finiteサイズ・offset、制約違反、重複・処理漏れ、例外で旧公開値を保持し、修正後に再試行できることを確認する。
- 後続の入力PRでは、例外を返すdirtyなlayoutでもCancel/FocusLost/UiKeyCancelInputがoverrideを再実行せず所有状態を解除し、修正後のlayoutが成功することを確認する。

## 実装・検証記録

2026-10-28、.NET SDK 10.0.401 / Linux x64で実装した。UIの87件がRelease/Debugの両方で成功した。先行のツリー・寿命の26件に、制約、継承した生成引数、縦横Stack、overflow、非表示部分木、Buttonの処理順、幅依存の高さ、無効化、失敗後の旧幾何保持と再試行、protocol/phase違反、別threadへの順次移行を含む61件を追加した。

Compositionの49件もReleaseで成功した。派生したcontentインデクサーが自身の型を返して継承警告を出さないことと、非公開の基底インデクサーには不要なnewを付けないことを回帰検証した。source/test/GeneratorのC#書式、Markdown、相対リンク、差分の空白を確認した。

UIと依存ライブラリのNuGet packを行い、ProjectReferenceを持たない二つのconsumerで確認した。一つは通常のusing staticからStackを構築し、overflowとworker上での順次layoutを実行した。もう一つはGenerator packageを明示参照し、別assemblyのPanelを継承した独自Composableコンテナーで、継承引数・型付きインデクサー・protectedなlayout helper・独自状態の無効化を実行した。Slangはリポジトリ指定の2026.19を使用し、GPU描画の実行・測定は行っていない。

Windows/Browserでの実環境確認、描画/入力との接続、layoutの性能・割当量の測定は後続検証とする。本記録はそれらの完了を示さない。

## 参考資料

- [Flutter: Understanding constraints](https://docs.flutter.dev/ui/layout/constraints)
- [BoxConstraintsのbox layout model](https://api.flutter.dev/flutter/rendering/BoxConstraints-class.html)
- [RenderObject.layoutの依存と無効化](https://api.flutter.dev/flutter/rendering/RenderObject/layout.html)
- [RenderFlexのlayout手順](https://api.flutter.dev/flutter/rendering/RenderFlex-class.html)
- [IntrinsicHeightの追加計測コスト](https://api.flutter.dev/flutter/widgets/IntrinsicHeight-class.html)

2026-10-28、固定内容への修正で子列の再設定・並べ替えを廃止した。上記の初期の検証件数は修正前の記録であり、現行契約はUI-0002と追加PRの検証結果に従う。
