# ADR-UI-0002: Widgetツリーの所有権とCompositionによる構築

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 1
- 更新: 2026-10-28

## 背景

[UI-0001](UI-0001-composition-based-ui-system.md)の保持型UIでは、通常内容と背景スロットを同じ所有ツリーとして扱う。Compositionが生成するインデクサーはsetterへ配列を渡すだけなので、親付け、検証、取り外し、寿命はUIが実装する。

## 決定

### 実装範囲と公開API

`src/UI/Lumyte.UI`、`tests/UI/Lumyte.UI.Tests`を追加し、solutionと既存の検証対象へ登録する。Compositionを通常参照し、Composition.GeneratorsをAnalyzer参照する。API diffの比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。

```diff
+namespace Lumyte.UI
+{
+    public sealed class UiSurface : IDisposable
+    {
+        // nullはArgumentNullException。親・surface付きrootはArgumentException。
+        // 作成スレッドへの固定なし。同時操作の同期は呼出し側が管理する。
+        public UiSurface(Compose.Definitions.Widget root);
+        public Compose.Definitions.Widget Root { get; }
+        // 冪等。接続を解除するがWidgetと外部資源を破棄しない。
+        public void Dispose();
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                protected Widget();
+                // 通常内容と背景で共通。rootはnull。
+                public Widget? Parent { get; }
+            }
+            [Composable]
+            public partial class Panel : Widget
+            {
+                public Panel();
+                // 既定空。setterがコピーし、変更不能なviewを返す。
+                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
+            }
+            [Composable]
+            public partial class Button : Widget
+            {
+                public Button();
+                // 0〜1個。構造違反は変更前に拒否。
+                [ComposeContent] public IReadOnlyList<Widget> Children { get; set; }
+                public IReadOnlyList<Widget> BackgroundChildren { get; }
+                // 空指定で背景を解除。複数の背景要素を許可する。
+                [ComposeSlot]
+                private static void Background(Button target, IReadOnlyList<Widget> children);
+            }
+        }
+    }
+}
```

生成されるファクトリ、子インデクサーと`Button.Background()`は手書きしない。このPRではlayout・Record・Dispatch・Clickを公開しない。

構築、子列の置換、背景スロットの適用ではlayoutを実行しない。後続のlayoutは[UI-0001](UI-0001-composition-based-ui-system.md)の共通契約に従ってこの所有ツリーを使い、希望サイズの保存領域やlayout専用の第二のツリーを追加しない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var root = Panel()[Button()[Button.Background()[Panel()]]];
using var surface = new UiSurface(root);
```

### 所有ツリーと置換手順

内部の子保存は親と役割（Content/Background）を持つ`ChildCollection`へ集約する。Panelを継承したカスタムコンテナーも同じChildrenを使う。Widgetへ任意の子を登録する第二の経路は設けない。走査順は背景、通常内容の順で、各列の記述順を維持する。

setterとBackground操作は次の手順で実行する。

1. 操作可能な実行phaseと、同じtreeの変更が再入していないことを確認する。
2. 入力列を新配列へコピーし、要素のnull・参照重複・個数を検査する。入力列自体のnullはArgumentNullException、その他の構造違反はArgumentException。
3. 自身・祖先の混入、別親、別スロット、別surfaceへの所属を拒否する。同じ置換対象列に既に属する子の再指定と並べ替えは許可する。
4. 全検証後に旧列から外れた部分木を切り離し、新列を親付けし、surface所属を部分木全体へ反映する。検証後のcommitはユーザーコードや資源取得を呼ばない。
5. layout無効化と、後続の入力実装が持つ状態解除の内部hookを呼ぶ。

失敗保証は一つの列の置換単位である。Compositionは通常内容を置換後にスロットを記述順に実行するため、後のスロット失敗時には先の成功が残る。同じ背景スロットを複数指定すると最後の成功が有効になる。

入力列のCount/indexerは利用側のコードを実行し得るため、コピー開始からcommit完了まで同じ所有treeの変更・接続・Disposeの再入をInvalidOperationExceptionで拒否する。未接続treeも変更中のadoption/接続を拒否する。入力評価の例外時には旧列を保ち、finallyで再入guardを解除して次の正当な変更を許可する。commitに必要な配列・view・部分木走査の確保は全て事前に済ませる。

公開する子リストはコピーした配列の変更不能なviewであり、入力列の後からの変更は反映しない。外した部分木の内部親子関係は保持する。別親へ移す場合は先に元の列から外す。surfaceのrootはDisposeで接続を解除してから別surfaceへ移す。

### スレッドとライフサイクル

UiSurfaceとWidgetを作成スレッドへ固定しない。接続中も、読取り・変更・Disposeが重ならなければ別threadへ順次処理を移せる。通常利用にowner thread判定、dispatcherや接続し直しを要求しない。利用者がTask.Runや独自thread等で明示的に並行実行する場合は、読取りを含む同時アクセスの同期と結果を利用者が管理する。

UI本体へlockや自動thread配送を追加せず、競合の検出・安全性を保証しない。既存の変更guardは同じcall stackからの再入を拒否するもので、thread間の排他制御ではない。将来の標準hostは通常処理の順序と、Window/IME等のthread依存serviceへの内部配送を管理し、その制約をCPU上のWidgetツリーへ転嫁しない。

内部phaseはIdle/Layout/Record/Dispatch/Disposedとし、後続PRが利用する。公開操作はtry/finallyでIdleへ復帰し、不正なphase・再入はInvalidOperationException、破棄後はObjectDisposedException。DisposeをLayout/Record/Dispatch中に呼ぶことも拒否する。Click中の通常の値・子の変更は許可するが、surface破棄はcallbackを抜けてから行う。

Root、Parent、子リストの読取にはthread検査を入れない。UiSurface.Rootは破棄後ObjectDisposedExceptionとし、Disposeは別threadからの順次呼出しも冪等である。Widgetは接続解除後も内部の親子関係を保持して利用できる。

本PRはIdle/Disposedの接続寿命を実装する。Layout/Record/Dispatchのphase遷移と無効化・入力解除のhookは、それぞれのAPIを導入する後続PRで接続し、将来APIのstubを公開しない。

Disposeは自身の入力状態をcallbackなしで解除するhookを持ち、全Widgetのsurface所属を外す。rootの内部親子関係、保持しているコールバック参照、外部リソースには触れない。不要なdelegate参照は利用側がプロパティへnullを代入して解除する。

## 検討した代替案

Composition側にParentや保存領域を追加すると汎用構築基盤の責務が変わる。通常内容と背景を別の所有モデルにすると二重配置を防げない。UI側の共通の子管理へ集約する。

作成threadへ固定する方式は、CPU上のツリーを直列に扱う場合にもdispatcherや再接続を要求するため採用しない。OS固有のthread要件は統合層の内部へ限定し、利用者が明示的に行う並行操作の責任は利用者へ委ねる。

## 結果と影響

生成コードの規則を保ったまま、安全な親子関係と部分木の再利用を実現できる。置換ごとに配列のコピーと検証コストが発生し、式全体のロールバックは提供しない。

## 検証方針

- 実際のAnalyzer参照で通常内容・背景混在、空スロット、繰り返し置換、新規instanceを確認する。
- null、循環、複数親・スロット・surface、同じ列の並べ替え、入力配列変更の遮断を確認する。
- 検証失敗で列を保つことと、後続スロット失敗時に先の成功を保つことを区別して確認する。
- 接続中の別threadへの順次移行、Disposeの冪等性、接続解除後の再利用、外部資源を破棄しないことを確認する。

## 実装・検証記録

2026-10-28、.NET SDK 10.0.401 / Linux x64で実装した。UIの25件がRelease/Debugの両方で成功し、既存Compositionの47件もReleaseで成功した。生成APIを別assemblyのテストから使用し、失敗後の旧列維持、slotの部分適用、参照同一性、入力評価中の再入、再接続、外部資源を破棄しないこと、20,000段の接続・解除を確認した。

solutionへsource/testを登録し、既存のsolution検証と全srcのpack列挙へ含めた。描画、入力、Signalは本PRの実装対象に含めない。Windows/Browserと後続機能の検証完了を示すものではない。

C#の書式検証とMarkdown検証を実施した。Composition/UIのNuGetパッケージを作成し、ProjectReferenceやGenerator参照を持たない別consumerから通常のusing static構文、背景slot、surface再接続が成功した。UIパッケージの実行時依存はCompositionのみである。

同日、固定thread検査を撤去し、接続中も別threadから順次操作できる契約へ緩和した。明示的な同時アクセスは利用側の責任とし、CPUツリーにthreadの所有者や自動dispatcherを持ち込まない。別threadでの通常内容/背景変更、await/workerをまたぐ順次操作、別threadでのDisposeと再接続を検証対象へ追加した。

固定thread検査を緩和した現行版では、UIの26件がRelease/Debugの両方で成功し、source/testの書式検証も成功した。
