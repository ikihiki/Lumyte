# ADR-UI-0002: Widgetツリーの所有権とCompositionによる構築

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 1、固定内容への修正はPR #79を前提とする追加PR
- 更新: 2026-10-29

## 背景

[UI-0001](UI-0001-composition-based-ui-system.md)の保持型UIでは、通常内容と背景を同じ所有ツリーとして扱う。利用者の指定により内容の設定を一回に限定し、任意のIReadOnlyList実装の読取りを起点とする再入を構造的に取り除く。親付け、検証、接続と寿命はUIが実装し、構築構文の生成はCompositionが担当する。

## 決定

### 公開APIと保存領域

最新修正の比較元はPR #79の`e6922285dfef533701cffdbbfb3f53656256cda0`。既存の可変内容APIを次のように変更する。[COMPOSITION-0004](../composition/COMPOSITION-0004-owned-content-collections.md)の所有コレクション初期化と型制約を利用する。

```diff
 namespace Lumyte.UI
 {
+    public sealed class WidgetChildren : IReadOnlyList<Compose.Definitions.Widget>,
+        ICompositionContent<Compose.Definitions.Widget>
+    {
+        // public constructorなし。Widgetが親と最大個数を指定して生成する。
+        public int Count { get; }
+        public Compose.Definitions.Widget this[int index] { get; }
+        public IEnumerator<Compose.Definitions.Widget> GetEnumerator();
+        // 初回成功のみ。入力をコピーし、空配列でも一回を消費する。
+        // 再指定は同じ値でもInvalidOperationException。検証失敗は回数を消費しない。
+        // null配列はArgumentNullException、構造違反はArgumentException。
+        public void Initialize(params Compose.Definitions.Widget[] content);
+    }
     public static partial class Compose
     {
         public static partial class Definitions
         {
+            [CompositionContentType(typeof(WidgetChildren))]
             public abstract class Widget
             {
-                protected Widget();
+                // 最大個数は非負。葉の既定は各0。ストレージ参照は固定。
+                protected Widget(int maximumChildren = 0, int maximumBackgroundChildren = 0);
+                protected internal WidgetChildren ContentCollection { get; }
+                protected internal WidgetChildren BackgroundCollection { get; }
                 public Widget? Parent { get; }
             }
             public partial class Panel : Widget
             {
-                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
+                [ComposeContent] public WidgetChildren Children { get; }
             }
             public partial class Button : Widget
             {
-                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
-                public IReadOnlyList<Widget> BackgroundChildren { get; }
+                [ComposeContent] public WidgetChildren Children { get; }
+                public WidgetChildren BackgroundChildren { get; }
-                private static void Background(Button target, IReadOnlyList<Widget> children);
+                private static void Background(Button target, Widget[] children);
             }
         }
     }
 }
```

WidgetChildrenはsealedで、IListなどの変更interface、setter、Add/Remove/Clearを公開しない。Compositionは生成したWidget[]をInitializeへ渡す。任意のIReadOnlyListやIEnumerableを受け取るoverloadと暗黙変換は設けない。直接初期化にも配列またはparamsを使い、必要なら呼出し側で明示的にToArrayする。

Compositionで生成するWidget派生型のComposeContentに配列、IReadOnlyList、その他の型を指定するとLYC001で生成を拒否する。派生型からこの制約を緩和できない。カスタムコンテナーはPanelを継承するか、基底constructorの最大数を指定し、ContentCollectionをget-onlyなComposeContentプロパティで公開する。任意の業務データ用コレクションfieldをC#自体で禁止することはしない。

### 初期化と所有権

通常内容と背景は独立に一回ずつ初期化できる。未初期化の読取りは空として扱い、読取りやsurface接続は初期化回数を消費しない。Panelは通常内容を複数、Buttonは通常内容を0〜1個・背景を複数受け入れる。

1. 操作phaseと未初期化状態を確認する。再初期化は入力内容にかかわらずInvalidOperationException。
2. 入力配列をコピーし、個数・null要素・参照重複を検査する。
3. 自身・祖先、親やslotに所属済みの子、surface接続済みrootを拒否する。
4. 接続対象の部分木と必要な配列・viewを全て事前確保し、利用者コードを呼ばず親・surface所属を一括設定する。
5. 内容を公開し、初期化済みにしてlayoutを無効化する。

検証・確保の失敗では親子関係も初期化状態も変更せず、正常な入力で再試行できる。成功後は同値・空・並べ替えを含む全ての再設定を拒否する。取り外し・別親への移動・背景の再適用は提供しない。Widgetの値やIsVisibleは引き続き更新できる。

生成された通常内容の初期化とslotの適用は別操作である。後のslot失敗時は先の成功を保持する。同じ背景slotを一式に複数回指定した場合、二回目を拒否し、初回の背景を保持する。slotだけの指定は通常内容を初期化しない。

### スレッド・再入・ライフサイクル

CPUのWidget/UiSurfaceは作成threadへ固定しない。読取りを含む明示的な並行アクセスの同期は利用者の責任とし、通常利用へlockやdispatcherを要求しない。

配列コピーから所有関係の反映までに外部のCount/indexerやcallbackを呼ばないため、rootの変更中フラグとMutationScopeを撤去する。変更可否の検査のためにrootまで走査しない。layout無効化に必要な祖先へのdirty伝播は維持する。Layout/Record中の値・子構成の変更と同一surfaceへの再入は、surfaceのphase検査で拒否する。将来、構造変更通知などのcallbackを導入する場合は、その境界の再入契約を別途定義する。

初回の子初期化はIdle/Dispatchで許可し、接続中なら部分木のsurface所属も反映する。layout・描画をその場で実行しない。DisposeはIdleのみ、冪等であり、通常内容・背景を含む全接続を解除する。Widget・外部資源・内部親子関係・初期化済み状態を保持し、root全体を別surfaceへ再接続できる。子の初期化回数は再接続でもリセットしない。UiSurface.Rootは破棄後ObjectDisposedExceptionとなる。

### 構築関数の再実行による動的な子の変更

動的な子の追加・削除・並べ替えは、Reactのように構築関数をもう一度実行して新しいWidget宣言を作り、UIが表示中の要素へ反映する方式とする。一回だけ設定する対象は各構築結果のWidgetChildrenであり、構築関数の呼出し回数ではない。既存のWidgetChildrenを再初期化しない。

```csharp
using Widget = Lumyte.UI.Compose.Definitions.Widget;
using static Lumyte.UI.Compose;

Widget Build(bool showDetails) => Panel()[
    showDetails ? Panel()[Rectangle(width: 80, height: 40)] : Panel()];
```

この例では状態が変わるとBuildを再実行し、新しいPanelとその一回だけ初期化された内容を返す。利用者は条件分岐や列挙を通常のC#で記述し、一般的な動的内容のために専用の変更可能コンテナーを実装する必要はない。IsVisibleは同じ要素を保持したまま可視性を変える用途に使える。

構築関数はUI宣言の生成を行い、再実行や不採用が可能な処理として扱う。外部I/O等の副作用は構築から分離し、UIが利用者コードの外部副作用をrollbackする契約にはしない。

再構築の内部管理は次の方針とする。

- 構築結果のWidget宣言と、継続する表示要素の識別・状態・寿命を分ける。reconcilerが内部の表示要素の子の対応を更新し、宣言側のWidgetChildrenは固定する。UIのlayout・入力・描画は同じ表示要素のツリーを参照する。
- 対応付けは同じ親・同じslotの範囲で行う。keyがある場合はkeyと型、keyがない場合は位置と型を基準にする。通常内容と背景のkey空間は分け、同じ兄弟列で重複したkeyはcommit前に拒否する。並べ替えで状態を追従させる要素には安定したkeyを使う。
- 対応した表示要素では、UIが管理する編集・選択・focus等の状態を保持する。型やkeyが変わった要素と除去された要素は寿命を終了し、新しい要素を開始する。構築関数内の通常のローカル変数や任意のWidget fieldを自動で状態として保持する契約にはしない。モデルや業務状態はSignal等へ保持する。
- Signal等の構造依存値を構築中に読み取った場合、UIが依存の追跡と再実行を自動管理し、一つの更新単位へまとめてlayout前に反映する。引数へ直接渡すSignalの値更新は、構造を変えなければプロパティ更新として扱える。利用者へruntime、EnterBuildや手動購読を要求しない。
- 構築と検証が成功してから表示中のツリーを切り替え、失敗時は直前の表示と状態を保つ。除去する要素のcapture・focus・IME・購読・アニメーションは、各サービスの寿命契約に従って終了させる。対応付けのkeyは、コネクテッドアニメーション用の共有keyと別の責務とする。

通常の子の動的変更はこの再構築経路で扱う。navigationやItemsもこの共通経路へ接続し、仮想化等の専用コンテナーはその機能上必要な場合に設ける。

本修正PRで実装済みなのは、一回初期化のWidgetChildrenと既存の静的ツリーの接続・layout・描画である。構築関数を受け取る公開API、key、表示要素の状態管理とreconciler、Signalによる構築の自動再実行は後続の実装範囲とし、そのADR・実装・テストを同じPRへまとめる。現在のParent/Size/Boundsと所有状態の保持方法も、その実装で宣言と表示要素の区別へ合わせて整理する。未実装の公開APIや動作を本PRへstubとして追加しない。

## 検討した代替案

setterを残して内部で一回に制限するだけでは、任意のリスト実装を受け入れるAPIが残る。readonlyな専用ストレージを公開し、初期化入力も配列へ限定する。init-only setterは構築後の生成インデクサーから呼べないため採用しない。

Widgetへの任意のcollection宣言を全て禁止すると、業務データまで制限してしまう。UIの所有ツリーは基底が作るストレージへ限定し、ComposeContentの型違反は汎用Generatorの型制約で検出する。

## 結果と影響

通常のusing static構築を維持しながら、内容の固定と入力配列のsnapshotを保証する。再入防止のroot走査を除去できる。同じ構築結果の子は固定されるが、構築関数の再実行とUI内部の照合によって動的な子の変更を実現する。宣言と表示状態の分離・照合には後続の実装が必要となる。既存のChildren setterと繰り返しslot適用に対する破壊的変更である。

## 検証方針

通常内容・背景・継承・with、空初期化、同値を含む二回目の拒否、失敗後の再試行、slotの部分成功、入力コピー、循環・重複・複数親・surfaceを検証する。接続中の初回初期化、別threadでの順次利用、Dispose後の内容保持、20,000段の接続・解除、Layout/Record中の初回初期化拒否も確認する。異なる型・setter付きComposeContentは生成テストで拒否する。

## 実装・検証記録

2026-10-28、固定内容への修正をADR・実装・テスト・READMEと同じ追加PRへまとめた。

- UI 79件とDrawing2D 93件がDebug/Releaseの双方で成功した。旧契約の置換・並べ替えテストを廃止し、一回初期化、失敗後の再試行、初回でもLayout/Record中は拒否すること、surface再接続で初期化回数を保持することを検証した。
- Composition 55件がReleaseで成功した。既存の代入型contentとの互換性、getter-only初期化と継承、型制約・readonly制約の違反を検証した。
- Linux x64のLavapipe上のVulkan/wgpuでGPUテスト2件が成功した。固定の子をIsVisibleで非表示にする連続frameの背景復元を確認した。record-onlyサンプルも成功した。
- 関連6パッケージをpackし、NuGet consumerで独自Widgetの生成・描画・二回目の初期化拒否・別threadでの順次更新が成功した。別のcollection型をComposeContentにしたconsumerは、継承した型制約に対するLYC001でbuildを拒否した。
- 対象C#の書式、Markdownと相対リンクを確認した。Windows・Browser・hardware GPUでの実行は未実施。

2026-10-29、動的な子の変更は構築関数の再実行とreconciliationで扱う方針へ修正した。各構築結果の一回初期化と、表示要素の状態・寿命を区別した。文書変更のみであり、上記の実装テストはreconcilerの完成を示さない。
