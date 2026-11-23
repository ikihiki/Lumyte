# Composition の最小例

`Compose.Definitions.Grid` 内で、ノードを操作する静的メソッドに属性を付ける。

```csharp
[ComposeAction]
private static void Column(Widget target, int value)
{
    ArgumentOutOfRangeException.ThrowIfNegative(value);
    target.AttachedValues["Grid.Column"] = value;
}
```

Generator が専用デリゲート型の静的ファクトリプロパティと、そのデリゲートへの `Column(int value)` 拡張を生成する。拡張は `Action<Widget>` を返し、with が生成ノードへ適用したときに元のメソッドを実行する。

```csharp
using Lumyte.Composition.Sample;
using static Lumyte.Composition.Sample.Compose;

var grid = Grid()[Text(with: [Grid.Column(1)])];
int column = (int)grid.Children[0].AttachedValues["Grid.Column"]!;
```

保存領域はサンプルの Widget が所有する。Lumyte.Composition は値を保存せず、保存形式も要求しない。負の値の拒否も宣言メソッドが適用時に行う。Get/Set/Clear は生成しない。

同じ属性で、プロパティだけを書き換えるメソッドや、追加引数がゼロ/複数の操作も扱える。サンプルに Fade、Reset、Tag を含めている。

```csharp
var text = Text(with: [Text.Fade(0.5f), Text.Tag("color", "red")]);
Text.Reset()(text);
```

## ビルドと実行

```sh
source tools/setup/activate.sh
mkdir -p artifacts/nuget
dotnet build Lumyte.slnx -c Release
dotnet test Lumyte.slnx -c Release --no-build
dotnet run --project samples/Lumyte.Composition.Sample -c Release --no-build
```

出力は `Children: 1, Column: 1`。

## プロジェクトからの参照

契約ライブラリは通常参照、Generator は Analyzer として参照する。このサンプルの csproj に具体例を記載している。

NuGet から使う場合は `Lumyte.Composition` と `Lumyte.Composition.Generators` を導入し、後者には `PrivateAssets="all"` を付ける。Generator パッケージは `analyzers/dotnet/cs` に DLL を配置する。外部への公開は行っていない。

初期対応は非 generic な public partial component を public static partial factory の public static partial Definitions 内に置く形である。ComposeAction メソッドは private 可、static void、参照型対象を先頭引数に取り、残りは通常の必須引数とする。async、generic、ref/out/in、params、optional、属性付き同名 overload は `LYC001` で報告する。このサンプルは UI の描画や入力を扱わず、Composition の構築・評価契約だけを実行する。

ジェネリックな定義は `ListView<int>()` として構築する。型に対応するデリゲートを取得して操作を追加する場合は、`ListViewFactory<int>().Select(2)` を利用する。

```csharp
var list = ListView<int>(with: [ListViewFactory<int>().Select(2)])[1, 2, 3];
```

名前付きスロットは静的メソッドに `[ComposeSlot]` を付けて定義する。

```csharp
var button = Button()[Button.Background()[Image("sample.jpeg")]];
var mixed = Button()[Text(), Button.Background()[Image("sample.jpeg")]];
```

子要素はスロット指定で捕捉し、外側のButtonへ適用する。保存領域と置換処理はButton側で定義する。

## 静的な構築関数

DefinitionsのGreetingとTypedは、クラスを定義せず静的関数へComposableを付けています。戻り値の基底WidgetがComposeFunctionAdapterでFunctionWidgetを指定し、生成された宣言をサンプルhostのEvaluateで実行します。生成factoryの呼出し自体はbodyを実行しません。

```csharp
var declaration = (Compose.Definitions.FunctionWidget)Greeting(key: "greeting");
Compose.Definitions.Widget result = declaration.Evaluate();
```

このhostはUIやOSなどの実行基盤を参照しません。状態や購読を保持する場合の寿命もhost側で定義します。

[関数factoryのADR](../../docs/adr/composition/COMPOSITION-0005-function-factories.md)

## 型付きテンプレート Slot

`CompositionTemplate` は 1〜16 引数を受け取って構成を返す delegate です。最後の型引数が戻り値の型になります。Slot の第二引数にこの型を指定すると、Generator が型付きラムダを受け取る入口を生成します。

```csharp
[ComposeSlot]
private static void Item(
    ListView<T> target, CompositionTemplate<T, ListView<T>, Widget> template)
    => target.ItemTemplate = template;

// using static Compose;
var list = ListView<int>()[
    4, 5,
    ListViewFactory<int>().Item()[
        (value, owner) => Text(content: $"{value}/{owner.Items.Count}")
    ]
];
```

Slot の適用は delegate を渡すだけで、評価はホストが `list.ItemTemplate!(4, list)` のように明示的に行います。自身を含むすべての引数はホストが渡し、自動注入・購読・結果のキャッシュは行いません。

固定の構成も同じインデクサーへ直接指定できます。サンプルでは `Button()[Button.Thumb()[Text(content: "fixed")]]` と書けます。式は指定時に評価され、引数を無視して同じ構成を返す delegate として保存されます。複製はしないため、複数接続先の所有権はホストの規則に従います。

入口は null の delegate と null の固定構成を拒否します。裸の null がオーバーロード間で曖昧になる場合は、C# の通常の規則に従って型を明示する必要があります。テンプレートの評価で返される null と例外はそのままホストへ渡します。
