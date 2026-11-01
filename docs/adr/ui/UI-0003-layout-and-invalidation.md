# ADR-UI-0003: 制約を下へ渡す一方向レイアウト

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 2、前提: PR 1
- 更新: 2026-10-28

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。Size / Bounds・layout hook・無効化はElementへ移し、制約とone-pass規則は維持する。
過去の実装記録とElement移行後の検証記録は分けて記載する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

[UI-0002](UI-0002-widget-tree-and-composition.md)の保持型ツリーをviewportへ配置する。広い制約で希望サイズを求め、後から別サイズへ配置する方式では、幅に応じて高さが変わるText等の内容を再計算する必要がある。サイズが確定する時点と、親が決められる範囲を明確にしたい。

Flutterのbox layoutを参考に、親が制約を渡し、子がその制約内の確定サイズを返し、親が子の位置を決める方式を採用する。Compositionによる構築を使用し、構築関数の再実行と表示要素の対応付けは[UI-0002](UI-0002-widget-tree-and-composition.md#構築関数の再実行による動的な子の変更)に従う。再構築とlayoutは別段階とし、layout・入力・描画で共通の表示要素のツリーを使う。宣言と表示状態の分離はUI-0002の最初の実装から使う。

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
+    public partial class UiElement
+    {
+        public Vector2 Size { get; }
+        public Rect2D Bounds { get; }
+        protected void InvalidateLayout();
+        protected virtual Vector2 LayoutOverride(UiBoxConstraints constraints);
+        protected Vector2 LayoutChild(UiElement child, UiBoxConstraints constraints);
+        protected void SetChildOffset(UiElement child, Vector2 offset);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // 既定null。指定値はfiniteかつ非負。
+                [ComposeParameter] public float? Width { get; set; }
+                [ComposeParameter] public float? Height { get; set; }
+                // 既定true。falseの部分木はlayout対象外、公開値を0にする。
+                [ComposeParameter] public bool IsVisible { get; set; }
+            }
+            [Composable]
+            public partial class Stack : Panel
+            {
+                public Stack();
+                // 既定Vertical。enum値が定義済みかの検証は行わない。
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

内部の非virtualなLayout入口が指定寸法、返値、子の処理漏れを検証し、利用側はLayoutOverrideだけを拡張する。カスタムコンテナーはPanelを継承してChildrenを所有し、次の順で処理する。

1. 子ごとの制約を決め、LayoutChildを呼んでサイズをローカル変数へ保存する。
2. 制約内で自身のサイズを計算する。
3. 保存した子サイズと自身のサイズから各子のoffsetを求め、SetChildOffsetする。
4. 自身のサイズを返す。呼び出し元の親が自身の位置を指定する。

サイズ決定は通常の再帰走査内で完結し、全ツリーの希望サイズ計算後に別の配置走査は行わない。親が子列を複数回ループして位置を計算することは許可するが、同じ可視子へ二回LayoutChildを呼んで別制約を試すことは初期契約では拒否する。LayoutChildの返値と子のSizeは今回の計算結果になる。SetChildOffsetは子のBoundsへ即時反映する。

直接の子以外、重複layout、位置の重複指定、layout前の位置指定、可視子の処理漏れはInvalidOperationException。nullはArgumentNullException、非finite offsetはArgumentOutOfRangeException。非表示の子はhelper内部で0として扱いoverrideを呼ばず、親が位置を指定しても公開Boundsは0へ正規化する。

LayoutChild/SetChildOffsetの失敗はpassへ記録し、custom override内で例外を捕捉してもpassを失敗として中断する。既に反映した結果は戻さない。宣言プロパティのsetterは値の保持だけを担い、layoutの無効化を行わない。InvalidateLayoutは表示側のruntime状態を変更する明示的な経路で使用し、宣言setterから呼ばない。phaseは検査しない。Size/Boundsのgetterと構築/子列の初回初期化は自動layoutを実行しない。

### 子コレクションとlayout

基盤は全ての登録済み子コレクションを検査するが、その名前や順序でlayoutを実行しない。コンポーネントのLayoutOverrideが各可視子を一回ずつlayout・配置する。ForegroundやEmbeddeds等にも同じprotocolを適用し、処理漏れはpass全体の失敗とする。非表示の部分木は全登録コレクションをたどって幾何を0にする。所有権の登録順とlayout順は独立である。

### 組み込みコンテナー

Panelは各可視子へ`constraints.Loosen()`を渡す。自身はboundedな軸ではmaxを使い、unboundedな軸では子のサイズの最大値を使ってConstrainする。子は原点0へ重ね、確定後のstretchは行わない。葉やコンテナーが空き領域を埋める必要がある場合は、その子へ最初からtight制約を渡すカスタムコンテナーで表現できる。

Stackは主軸を`[0, +Infinity]`、交差軸を`[0, 親max]`として各可視子をlayoutし、主軸のサイズ合計とgapを使って位置を決める。自身のboundedな軸はmax、unboundedな軸は内容の合計または最大値を使い、親制約内へConstrainする。交差軸の配置はstartとし、子を後から最大幅・高さへ伸ばさない。空Stackの内容サイズは0で、gapを加算しない。

例えば高さ100の縦Stackに高さ80の子を二つ置いたら、子はともに80のままで、二つ目はY=80になる。親の高さは100で、残り領域に合わせて二つ目を20へ縮めない。overflowはlayoutエラーではなく、viewportとClipToBoundsによる描画・hit testの制限で扱う。横Stackの非flexなTextへは無限の幅制約が渡るため、折り返したい場合はWidthか別のコンテナーで有限幅を事前に与える必要がある。

Buttonは通常内容へloose制約を渡して一度layoutし、そのサイズ（内容なしなら0）を自身の制約へConstrainする。その確定サイズをTightにして背景の全子をlayoutする。内容と背景のoffsetは0。背景のサイズをButton自身のサイズ計算へ戻さず、内容も後からstretchしない。

Rectangleは後続PRで導入し、boundedな軸はmax、unboundedな軸は0を選んでConstrainする。Width/Heightは先にtight化されるので明示寸法を優先できる。背景としてtight制約を受ければButton全体を埋める。

rootはviewportのtight制約とoffset0を持つ。非表示のrootだけはlayout対象外として公開Size/Boundsを0にする。合算はdoubleで行い、親の有限maxへclampできる自身のサイズはclamp後にfloatへ変換する。unboundedな自身のサイズや子offsetをfiniteなfloatで表せない場合はInvalidOperationExceptionとし、後続の子を潰したりInfinityを公開したりしない。

### 無効化と失敗時の公開状態

Width/Height・IsVisible・Orientation・Spacingの宣言setterはMarkLayoutDirty/InvalidateLayoutを呼ばない。利用者の値変更は構築関数を再実行し、新しい宣言を返す。継続する表示要素の無効化は、新旧宣言の照合とSignal/animationの専用更新処理が担当する。layoutへ影響する差分だけを無効化し、Fill等の描画だけの差分ではlayoutを要求しない。表示側からの無効化は自身からrootまでdirtyを伝播する。viewport変更・surface接続・runtime slotの公開・フォント等のruntime状態の変更も無効化の入口となる。surfaceへの再接続も新しいviewportで再layoutする。子列は通常内容・背景ごとに一回だけ初期化でき、同じ参照・同じ順序でも再初期化は拒否する。再構築の照合とElement保持は#77の共通基盤を使う。新しい構築結果は同じUiSurfaceのRebuild / UpdateTreeで反映し、既存宣言の直接書換えによる追従更新は提供しない。Signal / animationによる自動再実行はそれぞれの機能PRで追加する。手動の無効化APIを通常利用者へ要求しない。dirtyなsurfaceはrootからlayoutを開始し、各Elementで前回成功時の有効制約を比較する。自身がcleanかつWidth/Heightのtight化後の制約が同一なら、成功済みのサイズと子孫の幾何を再利用してLayoutOverrideと子孫の走査を省く。親は再利用した子にも今回のoffsetを指定する。子サイズへの依存を使ったrelayout boundaryは導入しない。

手書きElementのruntime状態がlayoutへ影響する場合は、変更時にprotected InvalidateLayoutを呼ぶ責務を持つ。これにより自身と祖先がdirtyになり、次のpassで再計算する。宣言setterへ無効化を追加せず、通常の利用者にはこの呼出しを要求しない。

一回のpassで求めたサイズとoffsetは各Elementへ直接保存する。作業tableに幾何を複製せず、一括公開・ロールバックを行わない。overrideの例外、制約違反、非finite値、子の処理漏れでは更新を中断し例外を呼出し元へ返す。旧状態の保持は保証しない。失敗時は対象surfaceのlayout cacheを無効化し、再試行で部分結果を成功済みの幾何として再利用しない。非表示にした部分木もcacheを失効させて幾何を0にし、再表示時に制約が同一でも再計算する。ホストは失敗した更新の描画・通常入力を停止し、破棄または原因を修正して明示的に更新し直す。overrideが不正なサイズを返した場合は黙ってclampせずInvalidOperationExceptionとする。

確定したlayout対象は処理中に変更しない。phaseやUpdateLayout再入の検査は行わず、hostが更新処理の順序を管理する。ホストは通常入力の配送前にUpdateLayoutを済ませ、Recordも必要な更新を先に行う。pointer Cancel、FocusLost、後続PRのUiKeyCancelInputは幾何を使わず、layoutを経由せず所有状態を解除する。破棄後の検査は取消にも適用する。CPU上のWidget/UiSurfaceは作成threadに固定せず、操作が重ならなければ別threadで順次利用できる。利用者が明示的に行う並行操作の同期は利用者が管理する。例外後の途中結果を使って通常入力・描画を継続しない。unmount後のElementの幾何は通常入力・描画に使わず、再接続時は新しい実体をlayoutする。固定の子列の取り外し・並べ替えは提供しない。

通常Dispatchはlayoutを実行しない。UpdateLayout/Recordの失敗でホストが入力配送を止める場合も、Idleへの復帰後にFocusLost等で清掃できる。取消は旧公開値も参照せず、dirtyとlayoutの再試行可能性を保つ。

実装PR 15の[UI-0019](UI-0019-signals-and-reactive-state.md)では、通常UI操作の入口とIdle復帰時にSignal依存の再構築と表示差分の反映を処理する。宣言setterの無効化には依存せず、専用更新経路から表示要素を無効化する。layoutの作業結果を公開する前に依存revisionも確認し、pass中の変更ではdirtyを残して更新を中断する。Size/Boundsの部分更新はrollbackせず、hostは描画を停止する。低水準利用を含め、利用者にSignalの手動pumpを要求しない。

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

一方、親のtight制約、unboundedな軸、overflowの理解が必要になる。通常layoutはdirtyな経路と各実行コンテナーの直接の子数に比例し、cleanな部分木の内部走査を省く。平坦なコンテナーの全子走査と入力幾何の更新は残る。子の処理漏れ・重複layout・offset漏れは、Element内の数値pass IDと完了・配置済みflagで検査し、passごとの辞書とEntryオブジェクトを作らない。pass IDは別surfaceにも同じ値を発行せず、独立ツリーの同時構築と逐次thread移動を妨げない。幾何は各Elementに一つだけ保持するが、成功制約とpass情報の常駐分が増える。性能と割当量は実測で評価する。

## 検証方針

- tight/loose、default、Tighten、Constrain、親制約優先、min/max逆転、NaN、各Infinityの許可・拒否を確認する。
- rootのtightサイズ、縦横Stack、空列、gap、非表示、Panelのbounded/unbounded軸、Buttonの内容→背景の順と確定サイズを確認する。
- 幅で高さが変わるfixtureで、実際の幅制約がlayout前に渡り、後から幅や高さを変更しないことを確認する。
- 高さ100に80+80を配置して二つ目のサイズを保つこと、clipの有無で描画とhit testの領域だけが変わることを後続PRと確認する。
- 別assemblyの型付きElement派生型で継承パラメーター、LayoutChildの返値、offset指定を使い、各可視子が一度だけlayoutされることを確認する。
- resize・初回内容設定・新しい宣言による表示切替、宣言setterが無効化しないこと、clean passの呼び出し回数を確認する。
- 非finiteサイズ・offset、制約違反、重複・処理漏れ、例外で更新を中断して先行ノードの結果を巻き戻さず、修正後に更新し直せることを確認する。
- 後続の入力PRでは、例外を返すdirtyなlayoutでもCancel/FocusLost/UiKeyCancelInputがoverrideを再実行せず所有状態を解除し、修正後のlayoutが成功することを確認する。

## 実装・検証記録

2026-10-29、#77の固定内容・phase検査を持たない基盤へ、一方向layoutを追加する形へ整理した。コード・テスト・READMEを同じ#78に含め、描画は#79の範囲とする。

## 参考資料

- [Flutter: Understanding constraints](https://docs.flutter.dev/ui/layout/constraints)
- [BoxConstraintsのbox layout model](https://api.flutter.dev/flutter/rendering/BoxConstraints-class.html)
- [RenderObject.layoutの依存と無効化](https://api.flutter.dev/flutter/rendering/RenderObject/layout.html)
- [RenderFlexのlayout手順](https://api.flutter.dev/flutter/rendering/RenderFlex-class.html)
- [IntrinsicHeightの追加計測コスト](https://api.flutter.dev/flutter/widgets/IntrinsicHeight-class.html)

独自のForeground/Embeddedsを含む全登録コレクションのlayout、処理漏れの拒否、非表示時の幾何リセットを含め、UI 78件のReleaseテストが成功した。

### 共通の再構築基盤との接続

UpdateLayoutは最初にUI-0002のUpdateTreeを実行し、新しい宣言を対応するElementへ採用してから幾何を計算する。UpdateTree単独は幾何を計算せず、配置に影響する設定・子構造の差分だけをElementから必要な祖先へ伝播する。状態準備失敗は旧ツリーを維持し、切替後のlayout失敗は既存の中断契約に従う。

## Element基盤への移行記録

2026-10-31、#78でSize / Bounds / dirty、layout hook、子layoutとoffsetの処理をElementへ移した。
Widgetは寸法・可視性・Stack設定のみを保持し、Panel / Stack / Buttonの実行処理は型付きElementへ置く。
同じ実体の設定差分をUiInvalidationへ分類し、同等の設定は幾何を再利用する。
Release UIテスト102件成功。制約、overflow、hidden、複数slot、無効なprotocol、
失敗時の部分結果と再試行、sequential thread利用、状態と幾何の継続を確認した。

### 成功済みlayoutの再利用

2026-11-01、#78から有効制約cacheと数値pass情報を適用する。未知のcustom Elementも同じprotocolを利用し、runtime状態の変更はInvalidateLayoutで宣言する。親が再実行された際もcache hitの子にはLayoutChildとSetChildOffsetを各一回要求し、重複や配置漏れの検査を省略しない。viewport変更は各子の有効制約が変わる場合だけ再計算し、固定寸法で有効制約が変わらない子は再利用できる。
