# Lumyte.UI.Collections

型付きItemsRepeater、単一選択ListBox、非編集ComboBox、遅延階層TreeView。UIとControlsを参照し、一覧は指定しない限り全項目を実体化する。Itemsは採用時にコピーし、KeySelectorと表示用のItem SlotまたはItemTemplateは必須。各templateは新しい未接続rootを返す。構築関数や独自のBuildContentが生成する子孫も公開前に検査し、別項目との共有や別実体への接続を拒否する。Signalによる部分更新でも検査を続け、同じ実体が保持する子孫は再利用できる。

Itemsの変更を採用するには新しい宣言を構築する。同じ宣言のsetterや元listの変更は自動監視しない。新しい宣言の採用では選択指定を静かに反映し、残るキーのscroll anchorと項目focusを復帰する。表示IDは新しくなる。

ListBoxの矢印とHome／Endはactive focusだけを移動し、Enter／Spaceとpointerで選択する。PageUp／PageDownは五項目分移動する。テンプレート内の入力部品は子優先の独立入力先となる。ScrollViewの有限viewportを使う。

ComboBoxはSelectedItemTemplate、EmptyTemplate、借用ICollectionPopupHostを明示する。Window型やfont、themeを暗黙取得しない。モデルへの書戻しはOnSelectionChangedで明示する。項目のキーはOptionKey、所有者はSelectionGroupとしてsnapshotへ公開し、接続IDとは区別する。既存SelectOptionAsyncは通常の項目hitと排他的leaseを使う。子のButtonやeditorが入力を受ける領域では、項目のClickを偽って公開しない。

## 固定長の仮想化

ListBoxのvirtualizationを指定すると、全件のデータとキーは保持し、表示範囲と予備項目だけを構築します。nullでは既存の全件構築を維持します。

```csharp
using Lumyte.UI.Collections;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

var list = ListBox<int>(items: Enumerable.Range(0, 10000).ToArray(),
    keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
    itemTemplate: _ => SizedBox(height: 24),
    virtualization: new UiItemVirtualization(24, 2));
```

ItemExtentは有限かつ正、Overscanは0〜1024です。縦横の固定間隔を使い、可変高さの測定はしません。接続したListBoxElementのBringIntoView(key)は前の項目を構築せず位置を予約し、次のUpdateLayoutで対象範囲を準備します。非同期版は明示dispatcherとモデル世代を検査します。AutomationのSelectOptionAsyncは通常の入力leaseと実際のwindow hitで対象キーを表示してからpointer操作します。

## 遅延階層

TreeView は Roots、KeySelector、ChildrenSelector、HasChildren、ItemTemplate を受け取ります。閉じた枝の children は取得しません。既知の全枝でキーの ordinal 一意性と祖先参照の循環を検証し、未知の枝まで検査済みとは扱いません。既知ノードは100万件、根からの深さは256までです。

ExpandedKeys と SelectedKey の受理値は TreeViewElement ごとに独立し、宣言へ暗黙に書き戻しません。ユーザー操作は採用後の OnExpandedChanged／OnSelectionChanged で明示的にモデルへ戻します。選択は同じ世代の collapse で維持し、focus は可視の祖先へ移します。

BringPathIntoView は根から対象まで最大257キーの明示経路を受け取り、子取得、展開と表示範囲の factory を一つの準備境界で検証します。GetByItemKey の入力による表示では既知の親索引、または任意の KeyPathSelector を使います。Observe の件数や assertion は children、path resolver と template を実行しません。接続 ID は再実体化ごとに新しくなります。

TreeView の左右キーは階層移動、上下と Home／End／Page は表示順の focus 移動です。本文クリックと Enter／Space は選択、展開記号は展開だけを行います。固定長 virtualization を明示すると ListBox と同じ bounded viewport と最大一件の入力保持枠を使います。

## 型付き Item Slot

ItemsRepeater、ListBox、ComboBox、TreeView の各具体的ファクトリは、値と受け取り側の宣言を渡す Item Slot を公開します。ComboBox は SelectedItem と Empty も提供します。Slot は同じ役割の旧 Func 引数より優先し、未指定なら旧引数を使います。

```csharp
ListBoxFactory<Person>().Item()[
    (person, owner) => Text(value: person.Name, layoutProvider: provider)
]
```

owner は現在の宣言であり、選択やスクロールの実体ではありません。生成は通常表示・仮想化・復元・ポップアップの既存の準備境界で行います。項目の根は従来どおり新鮮で交わらない必要があり、同じ固定 Widget を複数項目で使っても複製されません。Slot 設定は宣言の再構築にも引き継がれます。

## 読み取り専用 DataGrid

DataGrid は不変の `DataGridColumn<T>` と既存の型付き Header／Cell Slot を使います。列名は明示的なアクセシビリティ名で、表示文字から推定しません。文字組みサービスと font は呼び出し側が用意します。次の provider、people、Person 型の準備は省略しています。

```csharp
using Lumyte.UI.Collections;
using Collections = Lumyte.UI.Collections.Compose;
using Media = Lumyte.UI.Media.Compose;

var grid = Collections.DataGrid<Person>(
    items: people, keySelector: person => person.Id,
    columns: new[] { new DataGridColumn<Person>("name", "名前", 180) })[
    Collections.DataGridFactory<Person>().Header()[
        (column, owner) => Media.Text(value: column.Name, layoutProvider: provider)],
    Collections.DataGridFactory<Person>().Cell()[
        (cell, owner) => Media.Text(value: cell.Row.Name, layoutProvider: provider)]];
```

owner は評価先の具体的な DataGrid 宣言です。RegisterTemplateSlot によって引数シグナルでの再構築先へ再適用し、実体の選択・幅・並べ替えを宣言へ書き戻しません。行・列設定が同じまま表示テンプレートが変わった場合も受理状態を維持します。

Header の右端8論理単位は捕捉付き幅変更、本文クリックと Enter／Space は明示 `IComparer<T>` を持つ列だけの昇順／降順／元の順序です。ヘッダーの左右キーは8単位の幅変更です。セルの上下左右、Home／End、PageUp／PageDown はフォーカスだけを移動し、クリックと Enter／Space が単一行を選択します。セル編集・書戻しエンジンは持ちません。カスタムの Button／TextBox は子優先の通常入力先となり、二重選択しません。

固定の Widget を渡す Header()[widget]／Cell()[widget] も生成されますが、自動複製しません。一箇所だけの固定構成に使い、複数列・複数行・複数 Surface ではラムダから新しい未接続の Widget を返してください。ヘッダー内容は装飾、セル内容は通常の子入力という区別があります。

最大100万行と1024列、非空の ordinal キーと明示名は256文字までです。行と列は関数を呼ぶ前にコピーし、比較の同値行は元の行順を維持します。Column.Width は宣言値で、受理幅は DataGridElement.ColumnWidths の読み取り専用スナップショットです。不可視の列は構築せず、その列のソートは静かに解除します。

固定長の垂直仮想化は既存 ListBox の可視範囲＋overscanと最大一行の入力保持を使います。各可視行では全可視列を実体化し、列方向の仮想化はしません。ヘッダーを垂直 viewport 外へ置き、ヘッダーと行は同じ水平 ScrollView を共有します。行・セル reveal は表示範囲を準備してから位置を同時採用し、factory 例外では古い表示と位置を保ちます。

GetByRole(UiRole.DataGrid).GetByItemKey(rowKey).GetByColumnKey(columnKey) は実在セルを解決します。Observe は実体化・比較・スクロールをせず、入力 reveal は通常の排他的 lease と実際の window／modal／menu 範囲を使います。GridCell は読み取り専用で Fill を拒否し、明示した子 TextBox の入力能力とは分けます。診断 schema 12 がセル座標を公開し、schema 1〜11 の形状は保持します。
