# Lumyte.Composition

C# の宣言的構築を支える属性と `Optional<T>` の契約ライブラリ。値の保存、UI 型、アニメーション実行エンジンは含まない。

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

契約は .NET 10。初期 Generator は非 generic な public partial component と public static partial な外側/Definitions を扱う。操作メソッドは非 generic な synchronous static void とし、参照型の対象を先頭引数に取る。optional、params、ref/out/in、async、属性付き同名 overload は非対応。
