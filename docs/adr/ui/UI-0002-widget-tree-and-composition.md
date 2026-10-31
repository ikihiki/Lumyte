# ADR-UI-0002: Widgetツリーの所有権とCompositionによる構築

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 1（#77）
- 更新: 2026-10-29

## 背景

[UI-0001](UI-0001-composition-based-ui-system.md)の保持型UIでは、通常内容と背景を同じ所有ツリーとして扱う。利用者の指定により内容の設定を一回に限定し、任意のIReadOnlyList実装の読取りを起点とする再入を構造的に取り除く。親付け、検証、接続と寿命はUIが実装し、構築構文の生成はCompositionが担当する。

## 決定

### 公開APIと保存領域

比較元は#77のbaseとなる`docs/ui-composition-adr`。このrevisionにはLumyte.UIが存在しないため、以下は追加APIである。[COMPOSITION-0004](../composition/COMPOSITION-0004-owned-content-collections.md)の生成契約はmain向け独立PRで提供し、#77ではUIの所有コレクションへ適用する。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiSurface : IDisposable
+    {
+        // nullはArgumentNullException。親・surface付きrootはArgumentException。
+        public UiSurface(Compose.Definitions.Widget root);
+        // 破棄後はObjectDisposedException。
+        public Compose.Definitions.Widget Root { get; }
+        // 新しい切断済みrootを予約し、表示中のツリーはまだ変更しない。
+        public void Rebuild(Compose.Definitions.Widget root);
+        // 準備・検証に成功してから予約した宣言を切り替える。layout導入後も共通境界。
+        public void UpdateTree();
+        // 冪等。全接続を解除し、Widget・親子関係・外部資源を保持する。
+        public void Dispose();
+    }
+    public sealed class WidgetChildren : IReadOnlyList<Compose.Definitions.Widget>,
+        ICompositionContent<Compose.Definitions.Widget>
+    {
+        // public constructorなし。Widgetが親と最大個数を指定して生成する。
+        public int Count { get; }
+        public Compose.Definitions.Widget this[int index] { get; }
+        public IEnumerator<Compose.Definitions.Widget> GetEnumerator();
+        // 初回成功のみ。入力をコピーし、空配列でも一回を消費する。
+        // 再指定はInvalidOperationException。検証失敗は回数を消費しない。
+        // null配列はArgumentNullException、構造違反はArgumentException。
+        public void Initialize(params Compose.Definitions.Widget[] content);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            [CompositionContentType<WidgetChildren>]
+            public abstract class Widget
+            {
+                [ComposeParameter] public string? Key { get; set; }
+                protected virtual void OnReconcile(Widget previous);
+                protected Widget();
+                // 最大個数は非負。新しい所有コレクションを生成・登録する。
+                protected WidgetChildren CreateChildren(int maximumCount = int.MaxValue);
+                public Widget? Parent { get; }
+            }
+            [Composable]
+            public partial class Panel : Widget
+            {
+                public Panel();
+                [ComposeContent] public WidgetChildren Children { get; }
+            }
+            [Composable]
+            public partial class Button : Widget
+            {
+                public Button();
+                [ComposeContent] public WidgetChildren Children { get; }
+                public WidgetChildren BackgroundChildren { get; }
+                [ComposeSlot]
+                private static void Background(Button target, Widget[] children);
+            }
+        }
+    }
+}
```

WidgetChildrenはsealedで、IListなどの変更interface、setter、Add/Remove/Clearを公開しない。Compositionは生成したWidget[]をInitializeへ渡す。任意のIReadOnlyListやIEnumerableを受け取るoverloadと暗黙変換は設けない。直接初期化にも配列またはparamsを使い、必要なら呼出し側で明示的にToArrayする。

Compositionで生成するWidget派生型のComposeContentに配列、IReadOnlyList、その他の型を指定するとLYC001で生成を拒否する。派生型からこの制約を緩和できない。カスタムコンテナーはPanelを継承するか、CreateChildrenで生成したストレージをget-onlyなComposeContentプロパティで公開する。任意の業務データ用コレクションfieldをC#自体で禁止することはしない。

### コンポーネントごとの子プロパティ

WidgetはBackgroundやContent等の名前・役割を持たず、登録した子コレクションを所有権・接続・解除のために走査する。葉はコレクションを作らない。コンポーネントはconstructorでCreateChildrenを呼び、Children、BackgroundChildren、Foreground、Embeddeds等をget-onlyプロパティとして保持する。各コレクションの容量と初期化は独立である。CreateChildrenは毎回異なるストレージを生成し、負の最大個数は登録前にArgumentOutOfRangeExceptionとする。既存コレクションを他の親へ登録するAPIは設けない。

PanelはChildrenを一つ登録し、Buttonは容量1のChildrenと複数要素のBackgroundChildrenを登録する。Button.BackgroundはButton固有のComposition slotであり、基底Widgetの特別な機能ではない。カスタムslotも登録済みのWidgetChildren.Initializeへ配列を渡す。所有権走査の順序はlayout・描画・hit testの順序を定義しない。それぞれの順序と意味はコンポーネント側が定義し、基盤は全コレクションの子に共通の所有規則を適用する。

### 初期化と所有権

各コンポーネントが定義した子コレクションは独立に一回ずつ初期化できる。未初期化の読取りは空として扱い、読取りやsurface接続は初期化回数を消費しない。Panelは通常内容を複数、Buttonは通常内容を0〜1個・背景を複数受け入れる。

1. 未初期化状態を確認する。再初期化は入力内容にかかわらずInvalidOperationException。
2. 入力配列をコピーし、個数・null要素・参照重複を検査する。
3. 自身・祖先、親やslotに所属済みの子、surface接続済みrootを拒否する。
4. 接続対象の部分木と必要な配列・viewを全て事前確保し、利用者コードを呼ばず親・surface所属を一括設定する。
5. 内容を公開し、初期化済みにする。layoutの無効化は#78で接続する。

検証・確保の失敗では親子関係も初期化状態も変更せず、正常な入力で再試行できる。成功後は同値・空・並べ替えを含む全ての再設定を拒否する。取り外し・別親への移動・背景の再適用は提供しない。寸法・可視性などの値は#78で追加し、内容の固定とは別の契約で扱う。

生成された通常内容の初期化とslotの適用は別操作である。後のslot失敗時は先の成功を保持する。同じ背景slotを一式に複数回指定した場合、二回目を拒否し、初回の背景を保持する。slotだけの指定は通常内容を初期化しない。

### スレッド・再入・ライフサイクル

CPUのWidget/UiSurfaceは作成threadへ固定しない。読取りを含む明示的な並行アクセスの同期は利用者の責任とし、通常利用へlockやdispatcherを要求しない。

配列コピーから所有関係の反映までに外部のCount/indexerやcallbackを呼ばないため、rootの変更中フラグやMutationScopeを使用しない。変更可否の検査のためにrootまで走査しない。layout無効化に必要な祖先へのdirty伝播は維持する。宣言の変更をphaseで拒否する検査と、そのための状態enumは設けない。確定した描画対象を変更せず、次の構築結果を反映する順序をUIの更新処理で管理する。将来、構造変更通知などのcallbackを導入する場合は、その境界の再入契約を別途定義する。

初回の子初期化はphaseを検査せず、接続中なら部分木のsurface所属も反映する。layout・描画をその場で実行しない。Disposeは冪等であり、全ての登録済み子コレクションを含む接続を解除する。Widget・外部資源・内部親子関係・初期化済み状態を保持し、root全体を別surfaceへ再接続できる。子の初期化回数は再接続でもリセットしない。UiSurface.Rootは破棄後ObjectDisposedExceptionとなる。

現在の低水準APIでは、呼出し側が同じsurfaceのUpdateLayout・Record・Disposeを重ねず、layout・記録中に対象の宣言を書き換えない。phaseや同期的再入の検出は行わない。標準hostによる自動の処理順序管理と再構築・描画対象の確定は後続実装である。破棄済みsurfaceの使用、内容の二回目初期化、親子関係・値・layout protocolの検証は、それぞれの契約として維持する。

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
- 対応付けは同じ親・同じslotの範囲で行う。keyがある場合はkeyと型、keyがない場合は位置と型を基準にする。各コンポーネントの子プロパティごとにkey空間を分け、同じ兄弟列で重複したkeyはcommit前に拒否する。並べ替えで状態を追従させる要素には安定したkeyを使う。
- 対応した表示要素では、UIが管理する編集・選択・focus等の状態を保持する。型やkeyが変わった要素と除去された要素は寿命を終了し、新しい要素を開始する。構築関数内の通常のローカル変数や任意のWidget fieldを自動で状態として保持する契約にはしない。モデルや業務状態はSignal等へ保持する。
- Signal等の依存値を構築中に読み取った場合、UIが依存の追跡と再実行を自動管理し、一つの更新単位へまとめてlayout前に反映する。引数へ直接渡すSignalの値更新も構築関数の再実行と新しいツリーの確定へまとめ、描画中の既存Widgetへsetterを適用しない。利用者へruntime、EnterBuildや手動購読を要求しない。
- 構築と検証が成功してから表示中のツリーを切り替え、失敗時は直前の表示と状態を保つ。除去する要素のcapture・focus・IME・購読・アニメーションは、各サービスの寿命契約に従って終了させる。対応付けのkeyは、コネクテッドアニメーション用の共有keyと別の責務とする。

通常の子の動的変更はこの再構築経路で扱う。navigationやItemsもこの共通経路へ接続し、仮想化等の専用コンテナーはその機能上必要な場合に設ける。

PR #77で一回初期化のWidgetChildren、型/keyまたは型/位置の照合、component-ownedな状態の引き継ぎ、queued RebuildとUpdateTreeの更新境界を実装する。再構築の基盤をTextBoxやSignalの導入まで延期しない。layoutは#78でUpdateLayoutの先頭へ接続し、pointer captureは#80、keyboard/focusは#81、Automation IDは#84、focus bookmarkは#86で同じ照合結果へ接続する。TextBoxは#87で編集文書・選択・preeditをOnReconcileへ接続する。専用表示要素の全差分再利用とSignalによる構築の自動再実行は後続の対象であり、#77の状態保持とは区別する。

## 検討した代替案

setterを残して内部で一回に制限するだけでは、任意のリスト実装を受け入れるAPIが残る。readonlyな専用ストレージを公開し、初期化入力も配列へ限定する。init-only setterは構築後の生成インデクサーから呼べないため採用しない。

Widgetへの任意のcollection宣言を全て禁止すると、業務データまで制限してしまう。UIの所有ツリーは基底が作るストレージへ限定し、ComposeContentの型違反は汎用Generatorの型制約で検出する。

## 結果と影響

通常のusing static構築を維持しながら、内容の固定と入力配列のsnapshotを保証する。再入防止のroot走査を除去できる。同じ構築結果の子は固定されるが、構築関数の再実行とUI内部の照合によって動的な子の変更を実現する。宣言とcomponent-ownedな状態の照合は#77から実装し、描画要素の差分再利用と自動再実行は後続で追加する。最初の公開時からget-onlyな専用ストレージを使用し、置換可能なChildren setterを公開しない。

## 検証方針

通常内容・背景・継承・with、空初期化、同値を含む二回目の拒否、失敗後の再試行、slotの部分成功、入力コピー、循環・重複・複数親・surfaceを検証する。接続中の初回初期化、別threadでの順次利用、Dispose後の内容保持、20,000段の接続・解除、phaseによらない通常の値設定も確認する。異なる型・setter付きComposeContentは生成テストで拒否する。

## 実装・検証記録

2026-10-29、#77から一回初期化の専用コレクション、独立Composition契約の適用、phase検査のない所有ツリーを導入する形へ整理した。layoutと描画を含む後続機能は、対応するPRで追加する。

PR #77単独で状態保持9件を含むUI 29件のReleaseテストが成功した。Compositionは61件のRelease検証で、継承したslotの生成も確認した。型制約は`[CompositionContentType<WidgetChildren>]`で指定する。UIの実行時参照はCompositionのみで、layout・描画APIとGraphics依存を含めない。

### 最初の実装PRに含める状態保持

Rebuildは最新の宣言を予約する。UpdateTreeは同じ親と登録slotの型/key（keyなしは型/位置）で照合し、OnReconcileでcomponent-ownedな状態を準備してからrootを切り替える。key空間はslotごとに独立し、兄弟keyの重複は予約時に拒否する。新しい親へ移る場合も新しい寿命とする。構築・準備の失敗では旧ツリーを維持し、準備中に予約した次の宣言は次回更新まで保持する。準備中のDisposeでは再接続しない。照合は反復走査とし20,000段でも再帰しない。

カスタムcomponentはOnReconcileで明示的に状態を引き継ぐ。任意field、外部資源や副作用の自動コピー・rollbackは行わない。派生componentは必要な基底状態の引き継ぎも行う。入力captureの継続は入力PRで追加する能力契約に従う。UpdateTreeは幾何を計算しないため、layout導入後に直接呼んだhostは通常入力・記録の前にUpdateLayoutする。通常hostではUpdateLayoutが予約の適用も行う。

## 全体レビューに基づく更新境界

再構築の照合結果と旧新node集合をRebuildPlanへ集約する。CollectSubtreeは構造revisionに対応する一つの走査結果を再利用し、子collectionの初期化では全祖先のcacheを失効させる。再構築のためのcacheはWidgetとともに回収し、共有のglobal registryへ保持しない。

## 再構築候補の所有

予約rootはRebuildCandidate単位で保持し、同じ予約rootの再指定は何もしない。RebuildPlanが照合とcomponent状態の準備を担当し、surfaceは準備後の接続切替を担当する。入力導入後は公開後のcapture継続・取消通知を独立した引継ぎ処理へ置く。準備中に別rootを予約しても、現在の候補と次の候補の所有は混ざらない。

子のkey辞書はkeyを持つ子が存在するときだけ作る。keyなしのリストは位置照合で処理し、空の辞書やLINQ列挙の確保を省く。
