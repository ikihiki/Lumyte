# Lumyte.UI.Collections

型付きItemsRepeater、単一選択ListBox、非編集ComboBox。UIとControlsを参照し、全項目を実体化する。Itemsは採用時にコピーし、KeySelectorとItemTemplateは必須。各templateは新しい未接続rootを返す。構築関数や独自のBuildContentが生成する子孫も公開前に検査し、別項目との共有や別実体への接続を拒否する。Signalによる部分更新でも検査を続け、同じ実体が保持する子孫は再利用できる。

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
