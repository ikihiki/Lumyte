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

所有済みのストレージをget-onlyで公開する場合は、参照型に`ICompositionContent<T>`を実装します。値型および参照型と保証できない型引数は、boxingしたコピーだけが初期化されることを防ぐためLYC001で拒否します。生成インデクサーはプロパティを置換せず、`Initialize(T[])`へ配列を渡します。初期化回数・所有関係の検査・保持する入力のコピーは実装側の責務です。

基底classへ`[CompositionContentType<OwnedChildren>]`を指定すると、派生classを含めてComposeContentの型を完全一致に制限し、プロパティのsetterを禁止します。全基底型の制約を検査し、違反はLYC001です。既存の配列・標準collection interfaceへの代入は、この制約がないcomponentで引き続き使用できます。

[所有コレクションのADR](../../../docs/adr/composition/COMPOSITION-0004-owned-content-collections.md)

## Callback引数

`[ComposeParameter]`を付けた具体的delegateは型情報から自動判別し、生成factoryは`Action? onClick = null`等のdelegateを直接受け取ります。lambda・method group・delegate変数をcastなしで渡せます。nullableかつrequiredでないdelegateの省略と明示nullは、宣言の初期値にかかわらずnullを代入します。通常のOptional設定は従来どおり、省略時に初期値を保持します。

callbackは構築時には実行せず、引数の代入→with→内容の順を保ちます。継承・genericなcomponent、init-only property、private field、独自delegateにも対応します。非nullableなdelegateとrequired指定のdelegateは必須引数になります。System.Delegate・式木・delegate制約付き型パラメーターは通常のOptional/required設定として扱います。staticや書込み不能等の誤指定はLYC001です。

[Callback引数のADR](../../../docs/adr/composition/COMPOSITION-0002-callback-parameters.md)

## 静的な構築関数

`[Composable]`は`Compose.Definitions`のpublic static関数にも指定できます。関数の引数から外側のfactoryを生成し、関数の戻り値をhostが管理するwrapperへ格納します。既定値、具体的delegate、generic型制約を引き継ぎ、`key`を追加します。戻り値の基底型に`[ComposeFunctionAdapter(typeof(HostFunction))]`が必要です。既存のComposeArgumentAdapterがあれば、値引数もそのadapterで受け取ります。

UIのWidgetはこの契約を標準で提供します。CompositionはUIやSignalsに依存せず、具体的な実行・状態保持・寿命はhostの責務です。関数とクラスは同じDefinitionsへ混在できます。生成factoryの名前重複や非対応のasync/ref/params等はLYC001になります。

[関数factoryのADRと独立hostの利用例](../../../docs/adr/composition/COMPOSITION-0005-function-factories.md)
