# ADR-UI-0038: 遅延取得した階層をキーで展開する

- 状態: 採用
- 日付: 2026-11-18
- 基準コミット: `7bcf1bc256e226d04d7e2818893035be73ab9d2e`
- 変更関係: 一部変更
- 変更元: [UI-0013](UI-0013-component-catalog-and-delivery-plan.md#5-項目選択階層-8種類)
- 変更対象: TreeView の型付き階層、遅延取得、入力と固定長仮想化
- 継続する判断: Widget と Element の分離、単一選択、既存の所有権と入力経路

## 背景

階層を扱う利用者は、閉じた枝の内容まで列挙せず、表示する枝だけを展開したい。基準コミットの `src/UI/Lumyte.UI.Collections/ListBoxElement.cs` は固定長の一次元一覧を表示するが、親子関係と展開状態を持たない。

## 決定

Collections に型付き TreeView を追加し、実体ごとに既知のノード、展開キーと選択を保持する。表示中の階層を前順の行へ変換し、既存の固定長仮想化と所有権検証へ渡す。

### 利用者の変更前・変更後

変更前は ListBox の項目を利用者が平坦化し、親子関係と展開操作も別に実装する必要があった。

変更後は根と子取得を指定する。以下の例は文字列の根一件と子一件を扱い、表示内容は寸法だけを示す。

```csharp
using Lumyte.UI.Collections;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
var tree = TreeView<string>(roots: new[] { "root" }, keySelector: x => x,
    hasChildren: x => x == "root",
    childrenSelector: _ => new[] { "child" },
    itemTemplate: _ => SizedBox(height: 24),
    virtualization: new UiItemVirtualization(24));
```

### 階層の所有と検証

根と取得した子列を受付時にコピーする。子は親を初めて展開したときだけ取得し、同じモデル世代では閉じても既知の索引を保持する。キーは非空、最大256文字、ordinal で既知の全枝に一意とする。祖先と同じ参照の再登場も拒否する。未取得の枝について循環や一意性の検査済みとは扱わない。

既知ノードは最大100万件、深さは根を0として256までとする。宣言の再指定は新しい世代として再取得し、表示実体も作り直す。未知の展開キーを保持し、取得された時点で照合する。選択と keyboard focus は別の状態であり、同じ世代で閉じた枝の選択を消さない。新しい宣言の選択キーは、その提案で取得済みのモデルへ照合し、不在なら null とする。閉じた未知の枝を選択検証のためだけに探索しない。

展開要求は受理状態を直接変えず、UpdateLayout の未公開提案へ置く。所有 Surface が宣言、子テンプレート、兄弟の更新、配置を未公開準備している間は、展開と経路表示の再入を InvalidOperationException で拒否する。非同期要求の受付と所有スレッドへの配送にも同じ制約を適用し、resolver と子取得の実行中も再入させない。採用後の所有スレッド上の操作と通知からの次回要求は許可する。子取得、モデル検証と表示内容の準備が失敗した場合、旧表示と受理状態を維持する。callback は採用後に最新のものを一度呼び、callback の例外で採用を取り消さない。宣言からの値採用は通知しない。

### 入力とキー検索

上下、Home、End、Page は表示中かつ有効なモデル行へ focus を移し、選択は Enter、Space または行本文のクリックで変更する。右は閉じた親を展開し、開いた親では最初の有効な子へ移る。左は開いた親を閉じ、それ以外は親へ移る。展開記号の押下は一つの行所有者が捕捉し、本文選択と重複発火しない。独立した入力を持つテンプレート子は自身の入力を保持する。

閉鎖で隠れる focus は表示可能な祖先へ戻す。準備時点より新しい明示 focus 要求を上書きしない。非同期の編集 session 終了を待つ場合は既存の未公開仮想化境界で旧モデルと入力資源を保持する。

既知キーは親索引から経路を得る。未知キーの入力による表示要求だけが任意の KeyPathSelector を呼ぶ。利用者は根から対象まで最大257件の経路を返し、実体は各親子関係、可視性と有効性を検証してから祖先を展開する。全枝の探索は行わない。Observe の件数確認や assertion は resolver、子取得と factory を呼ばない。

経路の展開と表示位置は全 factory の成功後に一括採用する。非同期経路要求は enqueue 時にモデル世代と位置順序を確保し、後の wheel、同じ offset の ScrollTo と新しい表示要求を優先する。

### 通信の互換性

schema 11 に tree と treeitem の role、行の零始まり depth と parentKey を追加する。parentKey はモデルキーであり、未実体化の祖先へ架空の接続 ID を割り当てない。SelectionGroup は外側 TreeView の実際の接続 ID、OptionKey はモデルキーを表す。

schema 1〜10 は従来の field 集合を保持し、新 role を none へ投影する。キー表示、クリックと展開は入力 lease と実 window の遮蔽検査を経由する。新 role の検索を含む全命令は schema 11 を要求し、古い catalog へ送らない。

### 契約と API 差分

公開型は Lumyte.UI.Collections に配置する。描画、入力と接続の基本型は従来どおり Lumyte.UI が所有する。

```diff
+namespace Lumyte.UI.Collections
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public partial class TreeView<T> where T : notnull
+            {
+                public IReadOnlyList<T> Roots { get; set; }
+                public Func<T, string>? KeySelector { get; set; }
+                public Func<T, IReadOnlyList<T>>? ChildrenSelector { get; set; }
+                // 明示的なキー表示入力だけで呼ぶ。根から対象まで最大257件。
+                public Func<string, IReadOnlyList<string>>? KeyPathSelector { get; set; }
+                // 子列を取得せず展開可能性を返す。必須。
+                public Func<T, bool>? HasChildren { get; set; }
+                public Func<T, Lumyte.UI.Compose.Definitions.Widget>? ItemTemplate { get; set; }
+                public IReadOnlyList<string> ExpandedKeys { get; set; }
+                public string? SelectedKey { get; set; }
+                public Action<IReadOnlyList<string>>? OnExpandedChanged { get; set; }
+                public Action<string?>? OnSelectionChanged { get; set; }
+                // 既定16。有限非負で深さとの積も有限。
+                public float Indentation { get; set; }
+                // null は表示中の全行を構築する。
+                public UiItemVirtualization? Virtualization { get; set; }
+            }
+        }
+    }
+    public class TreeViewElement<T> where T : notnull
+    {
+        public IReadOnlyList<string> ExpandedKeys { get; }
+        public IReadOnlyList<string> VisibleKeys { get; }
+        public string? SelectedKey { get; }
+        // 既知かつ操作可能な親へ要求する。未知キーはfalse。Surface の未公開準備中は例外。
+        public bool SetExpanded(string key, bool expanded);
+        // 既知の表示行だけを対象とし、未知の閉じた枝を走査せず、明示経路がある場合だけ取得する。
+        public bool BringIntoView(string key);
+        // 経路表示と非同期配送も未公開準備中は例外。
+        public bool BringPathIntoView(IReadOnlyList<string> path);
+        public ValueTask BringPathIntoViewAsync(IUiDispatcher dispatcher, IReadOnlyList<string> path, CancellationToken cancellationToken = default);
+        public ValueTask SetExpandedAsync(IUiDispatcher dispatcher, string key, bool expanded, CancellationToken cancellationToken = default);
+    }
+}
```

階層の意味情報を基本 assembly と通信 DTO へ追加する。既存の ParentId は接続中の意味ツリーの親を維持し、論理階層は別の値とする。

```diff
+namespace Lumyte.UI
+{
+    public sealed record UiTreeItemState(int Depth, string? ParentKey);
+    public partial class UiElement
+    {
+        // 副作用を持たず、秘匿 subtree では読み取らない。
+        protected virtual UiTreeItemState? AutomationTreeItem { get; }
+    }
+    public sealed class UiAutomationNode
+    {
+        public UiTreeItemState? TreeItem { get; }
+    }
+}
+namespace Lumyte.Diagnostics.UI.Automation.Contracts
+{
+    public sealed record UiRemoteNode
+    {
+        // schema 11 だけで公開。旧 snapshot の field 集合は固定 DTO で保持。
+        public int? Depth { get; init; }
+        public string? ParentKey { get; init; }
+    }
+}
```

## 検討した代替案

- 全階層を事前列挙する: 一意性を全体で確認できるが、閉じた枝の取得まで必要となるため採用しない。
- 外部 UI framework を接続する: 階層表示を再利用できるが、既存の入力、接続 ID と所有権を変換する必要がある。既存 ListBox の実体化基盤を使う。
- 描画だけを省略する: 画面外の内容構築を減らせないため、固定長の表示範囲だけを実体化する。

## 結果と影響

利用者はキーと子の有無を副作用なく供給し、取得結果が世代中に変わる場合は宣言を再指定する責務を持つ。未取得の枝を暗黙に探索しないことで、監視操作から予期せぬ取得を発生させない。

検証では遅延取得回数、重複と循環の拒否、失敗時の旧表示、独立した複数実体、表示範囲の構築件数を確認する。物理画面、OS IME と別 RID の実行結果は managed な階層テストと区別する。
