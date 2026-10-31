# ADR-COMPOSITION-0005: 静的な構築関数のfactoryとhost adapter

- 状態: 採用・実装済み
- 日付: 2026-10-30
- 対応: main向けComposition拡張PR。UIの実行・状態保持は別PRで接続する。
- 前提: COMPOSITION-0001〜0004

## 背景

利用側が定義する部品を、静的関数の引数と戻り値だけで表現したい。関数を即時実行するだけでは、利用側が識別・状態の照合・実行時期を制御できない。Compositionは型付きの宣言と呼出しコードを生成し、実行と状態管理をhostへ委譲する。

## 決定

### 定義と生成API

既存のpublic static partialな`Compose.Definitions`へ、`[Composable]`を付けたpublic static関数を宣言できるようにする。クラス方式も維持し、両者を混在できる。外側クラス名とfactory名は既存のComposable.Factory・Name、CompositionDefaultsで指定する。

```csharp
// Compose.Definitions内。WidgetとFunctionWidgetは利用側が定義する。
[Composable]
public static Widget Greeting(string? label = "hello", Action? onBuild = null)
{
    onBuild?.Invoke();
    return Compose.Text(content: label);
}

// using static Sample.Compose;
var declaration = Greeting(label: "sample", key: "greeting");
// この時点では関数bodyを実行しない。実行時期はhostが選ぶ。
```

Generatorは関数ごとに異なる非公開sealed classと外側の静的factoryを生成する。wrapperはhost adapterを継承し、Buildから元の定義関数を型付きで呼ぶ。factoryの戻り値はhostのBuild戻り値型であり、内部wrapper型を公開APIへ要求しない。

関数の引数名・順序・型・既定値・generic型制約を引き継ぐ。値引数にadapterがなければ元の型、ComposeArgumentAdapterがあれば閉じたadapter型を使う。具体的delegateはそのまま受け取る。末尾へ`string? key = null`を追加し、hostのKeyへ設定する。既定値は生成先から解決できるよう型付きの定数として出力し、decimal・浮動小数・nullable enum・特殊値も扱う。

### hostの契約

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ComposeFunctionAdapterAttribute(Type adapterType) : Attribute
{
    public Type AdapterType { get; } = adapterType;
}
```

関数の戻り値の型、またはその基底型に属性を付ける。指定するadapterは次を満たす。

- publicな非generic abstract classである。
- publicまたはprotectedな引数なしconstructorを持つ。
- protected abstractな引数なし・非genericのBuildを宣言する。
- 関数の戻り値とadapter自身をBuildの戻り値へ暗黙変換できる。
- 公開書込み可能なstring Keyを持つ。生成factoryのnullを受け取れるよう、nullableまたはnullable未指定の契約で宣言する。
- Build以外に、生成wrapperが実装する必要のあるabstract memberを要求しない。

hostは任意の時期にBuildを呼ぶ。UIツリー、状態表、購読、layout、描画、thread制御はCompositionへ追加しない。キーの照合や保持する状態の寿命もhostが実装する。サンプルのFunctionWidgetは公開EvaluateからBuildを呼び、UIを参照せず生成結果を実行する。

### 値adapterと再構築recipe

戻り値の基底型のComposeArgumentAdapterを既存と同じ規則で解決する。関数の非delegate値引数をそのadapterで受け取り、GetInitialValueで具体値を取得してwrapperへ保存し、AttachToで受動的な情報を登録する。関数引数用adapterも[COMPOSITION-0003](COMPOSITION-0003-argument-adapters.md)の完全一致・generic定義・共通指定の順で選び、各宣言型に一致するIComposeArgumentを実装する。選択後の型引数の制約も検証する。

省略可能なadapter引数のdefaultは未指定を表し、定義関数の既定値を使う。具体型として渡したnull/defaultは指定値として扱い、必要ならadapter側のConstant等で指定する。必須引数の未指定はGetInitialValueで失敗する。

wrapperがICompositionRebuildableを実装する場合、元の引数とkeyから新しい宣言を作るrecipeを登録する。recipeの実行や子の所有関係の再構築をGeneratorが自動で開始しない。factory自身は関数body・callbackを実行しないが、hostがBuildを呼んだ後は定義関数内に書いた処理が通常のC#として実行される。

### 診断と非対応範囲

不正宣言は既存のLYC001で拒否する。async、非public/non-static、ref戻り値、ref/out/in・params、ref struct、非公開のパラメータ型、予約引数名key・__始まりは非対応とする。型パラメータの公開制約を引き継ぎ、allows ref structは拒否する。重複factory名はクラス方式を含めて拒否し、overloadはComposable.Nameで別名を付ける。

実行時reflectionやDynamicInvoke、新しいruntime、暗黙の外部状態保持を導入しない。Buildが返した値の検証や例外・副作用の扱いはhostの責務である。

## 互換性

既存のクラス方式と静的操作・slotは維持する。関数へのComposable指定を追加し、既存のクラスでadapterを指定しない場合の値引数の型は変えない。delegate引数の変更はCOMPOSITION-0002の契約に従う。UI接続をmainへ同時に入れることは前提にしない。

## 配置と検証

契約をLumyte.Composition、生成をComposition.Generatorsへ置き、両方を既存packageとして配布する。契約はBCLのみ、GeneratorはRoslynのみを参照する。main向けPRには関連するCOMPOSITION-0002〜0004の契約・生成・テストも含め、UIスタック側はこれらを前提にする。

- UIを参照しない独立consumerのcompilationで、既定値、delegate、generic制約、overload、LYC001を確認する。
- 別assemblyの[サンプルhost](../../../samples/Lumyte.Composition.Sample/README.md)で、実行の遅延、明示null、callback、recipeによる新しい宣言、閉じたgeneric関数の実行を確認する。
- 既存の所有コレクション・型制約・callback・値adapterの回帰テストを実行する。
- strict NativeAOTでサンプルをpublishして実行し、契約とAnalyzerをpackする。

## 代案と結果

戻り値を即時に構築する通常の関数だけでは、hostが状態を照合してから関数を評価できない。GeneratorへUI型や外部状態の管理を組み込むとCompositionの独立性を失う。生成wrapperをhostのabstract classへ適応させる方式により、実行時の責務を利用側へ残す。
