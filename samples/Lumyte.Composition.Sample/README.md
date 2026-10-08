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

初期対応は非 generic な public partial component を public static partial factory の public static partial Definitions 内に置く形である。ComposeAction メソッドは private 可、static void、参照型対象を先頭引数に取り、残りは通常の必須引数とする。async、generic、ref/out/in、params、optional、属性付き同名 overload は `LYC001` で報告する。Browser、AOT、trimming は未検証。
