# ADR-UI-0002: Widgetツリーの所有権とCompositionによる構築

- 状態: 提案
- 日付: 2026-10-27
- 対応: 実装PR 1

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
+        // 接続先のスレッドをこの呼び出しのスレッドへ固定する。
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

構築、子列の置換、背景スロットの適用ではlayoutを実行しない。後続の一方向layoutはこの所有ツリーへ制約を渡し、親が子の確定サイズと位置を扱う。構築時に希望サイズの保存領域やMeasure/Arrange用の第二のツリーは追加しない。

```csharp
using Lumyte.UI;
using static Lumyte.UI.Compose;

var root = Panel()[Button()[Button.Background()[Panel()]]];
using var surface = new UiSurface(root);
```

### 所有ツリーと置換手順

内部の子保存は親と役割（Content/Background）を持つ`ChildCollection`へ集約する。Panelを継承したカスタムコンテナーも同じChildrenを使う。Widgetへ任意の子を登録する第二の経路は設けない。走査順は背景、通常内容の順で、各列の記述順を維持する。

setterとBackground操作は次の手順で実行する。

1. 操作可能なスレッドと実行phaseを確認する。
2. 入力列を新配列へコピーし、要素のnull・参照重複・個数を検査する。入力列自体のnullはArgumentNullException、その他の構造違反はArgumentException。
3. 自身・祖先の混入、別親、別スロット、別surfaceへの所属を拒否する。同じ置換対象列に既に属する子の再指定と並べ替えは許可する。
4. 全検証後に旧列から外れた部分木を切り離し、新列を親付けし、surface所属を部分木全体へ反映する。検証後のcommitはユーザーコードや資源取得を呼ばない。
5. layout無効化と、後続の入力実装が持つ状態解除の内部hookを呼ぶ。

失敗保証は一つの列の置換単位である。Compositionは通常内容を置換後にスロットを記述順に実行するため、後のスロット失敗時には先の成功が残る。同じ背景スロットを複数指定すると最後の成功が有効になる。

子リストは変更不能なviewとして公開し、コピー元を後で書き換えても反映しない。外した部分木の内部親子関係は保持する。別親へ移す場合は先に元の列から外す。surfaceのrootはDisposeで接続を解除してから別surfaceへ移す。

### スレッドとライフサイクル

UiSurfaceは作成スレッド専用で、接続中のWidget操作にも同じ制約を適用する。未接続の部分木は利用側が同期して単一スレッドで操作でき、接続時にsurfaceの制約を適用する。UIは未接続ツリーの同時操作を検出・同期する保証を持たない。

内部phaseはIdle/Layout/Record/Dispatch/Disposedとし、後続PRが利用する。公開操作はtry/finallyでIdleへ復帰し、再入・別スレッドはInvalidOperationException、破棄後はObjectDisposedException。DisposeをLayout/Record/Dispatch中に呼ぶことも拒否する。Click中の通常の値・子の変更は許可するが、surface破棄はcallbackを抜けてから行う。

Disposeは自身の入力状態をcallbackなしで解除するhookを持ち、全Widgetのsurface所属を外す。rootの内部親子関係、保持しているコールバック参照、外部リソースには触れない。不要なdelegate参照は利用側がプロパティへnullを代入して解除する。

## 検討した代替案

Composition側にParentや保存領域を追加すると汎用構築基盤の責務が変わる。通常内容と背景を別の所有モデルにすると二重配置を防げない。UI側の共通の子管理へ集約する。

## 結果と影響

生成コードの規則を保ったまま、安全な親子関係と部分木の再利用を実現できる。置換ごとに配列のコピーと検証コストが発生し、式全体のロールバックは提供しない。

## 検証方針

- `CompositionConstructionTests`: 実際のAnalyzer参照で通常内容・背景混在、空スロット、繰り返し置換、新規instanceを確認する。
- `TreeOwnershipTests`: null、循環、複数親・スロット・surface、同じ列の並べ替え、入力配列変更の遮断を確認する。
- `TreeReplacementTests`: 検証失敗で列を保つことと、後続スロット失敗時に先の成功を保つことを区別して確認する。
- `SurfaceLifetimeTests`: 作成スレッド、Disposeの冪等性、接続解除後の再利用、外部資源を破棄しないことを確認する。
