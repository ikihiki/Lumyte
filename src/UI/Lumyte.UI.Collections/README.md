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
