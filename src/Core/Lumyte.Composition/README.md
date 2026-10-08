# Lumyte.Composition

C# の宣言的構築を支える属性、`Optional<T>`、名前付きスロットの契約ライブラリ。値の保存、UI 型、アニメーション実行エンジンは含まない。

`Lumyte.Composition.Generators` をビルド時の Analyzer として導入すると、Composable クラスから専用デリゲート型のファクトリプロパティと子要素インデクサを生成する。

```csharp
using static Example.Compose;

var grid = Grid()[Text(with: [Grid.Column(1)])];
```

定義型は `Compose.Definitions.Grid`、同名のファクトリプロパティは外側の `Compose.Grid` に置く。定義クラス内の静的 void メソッドへ `ComposeAction` を付けると、そのデリゲート型への拡張メソッドを生成する。

```csharp
[ComposeAction]
private static void Column(Widget target, int value)
{
    target.AttachedValues["Grid.Column"] = value;
}
```

拡張は `Action<Widget>` を返し、構築したノードへ適用したときに元のメソッドを呼ぶ。保存領域・検証・通知などは Widget とユーザーのメソッドが管理する。スタイル書き換えやアニメーション開始も静的メソッドとして表現できる。

契約は .NET 10。初期 Generator は generic を含む public partial component と public static partial な外側/Definitions を扱う。操作メソッドは非 generic な synchronous static void とし、参照型の対象を先頭引数に取る。optional、params、ref/out/in、async、属性付き同名 overload は非対応。

ジェネリック型は `ListView<int>()` の静的メソッドと `ListViewFactory<int>()` のデリゲート取得メソッドを生成し、型制約を引き継ぐ。

名前付きスロットは `[ComposeSlot]` を付けた静的メソッドから生成する。先頭引数は定義型自身、第二引数は子要素の配列または対応 collection interface とする。

```csharp
[ComposeSlot]
private static void Background(Button target, IReadOnlyList<Widget> children)
    => target.BackgroundChildren = children;

// using static Compose;
var button = Button()[Button.Background()[Image("sample.jpeg")]];
```

スロット指定は子要素を捕捉し、外側のインデクサーで対象へ適用する。保存と置換は利用側メソッドの責務。通常の子要素との混在やジェネリックなスロットにも対応する。
