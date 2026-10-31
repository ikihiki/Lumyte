# ADR-COMPOSITION-0002: delegate型の自動判別と直接引数

- 状態: 採用
- 日付: 2026-10-27
- 対応: main向けComposition拡張PR。UIへの接続はUIスタック側で行う。

## 背景

[COMPOSITION-0001](COMPOSITION-0001-declarative-composition.md)は省略可能な設定値をOptionalで受け取り、省略と明示的nullを区別する。一方、`Button(onClick: () => Save())`のラムダ・メソッドグループにはdelegate型の引数が必要であり、Optionalで包んだ引数ではこの利用構文を直接提供できない。

イベントコールバックをファクトリ引数として渡す利用側では、省略・nullともにハンドラーなしにする必要がある。具体的なdelegate型からこの生成契約を判別し、通常の設定値の省略規則は維持する。

## 決定

### 公開API

比較元はmain `4773a2b73a3a9a1a2b1d0b5e6c99364eaf070150`。`ComposeParameterAttribute`にプロパティを追加せず、既存の`[ComposeParameter]`へ指定されたmemberの型情報から具体的なdelegateを自動判別する。Action、`Action<T>`、`Func<T>`、publicな独自delegateが対象である。UI型・イベント実行・購読管理をCompositionへ追加しない。

| memberの型・指定 | 生成する引数 | 省略 |
| --- | --- | --- |
| nullableな具体的delegate | 元のdelegate型、既定null | 可 |
| 非nullableな具体的delegate | 元のdelegate型 | 不可 |
| requiredな具体的delegate（nullableを含む） | 元のdelegate型 | 不可 |
| その他の通常設定 | 従来のOptionalまたはrequired引数 | 従来どおり |

System.Delegate、式木、delegate制約付き型パラメーターは具体的な呼出しsignatureを持たないため、自動判別の対象にせず通常設定として扱う。static/readonly・アクセス不能・名前衝突等の既存検証は維持する。

### 生成シグネチャと構築順

具体的なdelegateのメンバーはOptionalで包まず、元のdelegate型を直接ファクトリ引数へ生成する。nullableかつrequiredでない場合だけ`= null`を付ける。デリゲート型ファクトリ、generic型の静的ファクトリと専用delegate、生成コンストラクター・helper間で同じ引数型を使う。型引数・nullable・引数名は従来どおり保持する。必須引数を先に置き、その中は従来のmember順を維持する。非nullableなdelegateも必須として並べ、withは末尾に維持する。

```csharp
[ComposeParameter]
public Action? OnClick { get; set; }

// 外側ComposeとDefinitionsを使う既存の定義形式で宣言する。
var button = Button(onClick: () => Save());
var other = Button(onClick: Save);
var none = Button();
var cleared = Button(onClick: null);
```

上記の宣言を持つButtonだけを定義した場合の生成APIは次の形になる。

```diff
+public static partial class Compose
+{
+    public static Definitions.ButtonFactory Button { get; }
+    public static partial class Definitions
+    {
+        public delegate Button ButtonFactory(Action? onClick = null,
+            IReadOnlyList<Action<Button>>? @with = null);
+    }
+}
```

通常の初期化子と引数なしコンストラクターの後、生成コンストラクターはcallbackの値を一度代入する。省略も明示的nullもnullを代入し、メンバーの非nullな初期値を保持しない。これはnullableな具体的delegateに共通の契約とする。必要なdelegateは非nullableまたはrequiredで宣言し、呼出し側が明示する。

withは全引数の代入後、子インデクサーはその後に実行する。Generatorはcallbackを実行・追加購読せず、delegate参照をコピーするだけとする。callback型がAction以外でも、完了・戻り値・例外の扱いは利用側の責務である。非同期の自動await、イベントdispatcher、購読tokenは生成しない。

### 互換性と失敗

具体的なdelegate以外では従来のOptional・required・init・初期値・withの契約を変えない。具体的なdelegateを宣言済みのfactoryは生成signatureと省略時の意味が変わるため、API変更として扱う。未マージのCallbackフラグは撤去し、opt-in/opt-outは設けない。

不正宣言はGeneratorのLYC001でbuildを失敗させる。setterやコンストラクターの例外は従来どおり構築時に伝播し、callbackは実行しない。契約ライブラリはBCLのみ、Generatorはbuild時のAnalyzerという依存を維持する。

## 検討した代替案

- 呼び出し側へActionへのcastや一時変数を要求すると、直接ラムダでUIを宣言する構文が煩雑になる。
- 明示的なCallbackフラグは型情報と重複するため設けない。delegate型から生成規則を決め、記述の重複を避ける。
- callbackをrequiredにすれば直接delegate型になるが、ハンドラーを持たないButtonにも明示引数が必要になる。
- ComposeActionでイベント購読すると、構築パラメーターによる設定・置換とは異なる追加購読と解除の仕組みが必要になる。

## 結果と影響

ラムダ、メソッドグループ、delegate変数を自然な名前付き引数として渡せる。Optionalで省略を区別する通常設定とは契約が異なるため、利用側はcallbackの初期値をnullとするのが基本になる。UI本体に専用の生成コードや手書きファクトリは不要となる。

## 検証方針

- 実Generatorを使い、lambda、method group、delegate変数、省略、nullを渡す別assemblyのconsumerをコンパイル・実行する。
- 引数は代入だけで実行されず、with→子の順を保つこと、init・継承・generic型・独自delegateでも同じ型を生成することを確認する。
- 通常設定のOptional・初期値保持を回帰検証し、nullable delegateでは省略・明示nullともにnullとなることを確認する。非nullable/required delegateは必須引数となり、省略可能な引数より前へ配置されることも確認する。
- delegate以外・式木・System.Delegate・型パラメーターが従来どおり扱われ、不正なstorageはLYC001で拒否することを確認し、NuGetの契約とAnalyzerを組み合わせたconsumerでも生成を確認する。

## 参考資料

- [C#の匿名関数とメソッドグループの変換](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions#10215-anonymous-function-conversions-and-method-group-conversions)

## 実装・検証記録

2026-10-29、元のUIスタックで型情報による自動判別と直接delegate引数を実装した。2026-10-30、UI非依存の契約・生成・テストをmain向け独立PRへ移した。Composition 70件（Release）で、通常Optionalの回帰、継承/init/private field/generic/独自delegate、構築順、LYC001を確認した。UI 120件（Release）とNuGetだけを参照する独自コンポーネントでも生成・実行に成功した。生成側へUI型や実行時の購読・callback配送は追加していない。
