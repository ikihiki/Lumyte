# Lumyte.Diagnostics.Generators

Operation メソッドの opt-in 属性一つから、スキーマと反射を使わない配送コードを生成する Incremental Generator。

```xml
<ProjectReference Include="../Lumyte.Diagnostics.Generators/Lumyte.Diagnostics.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## 最小宣言

```csharp
public sealed partial class InputDiagnostics(InputOverrideService input)
{
    [DiagnosticOperation(DiagnosticPermission.OverrideInput)]
    private DiagnosticResult<ButtonOverrideReceipt> OverrideButton(
        DiagnosticOperationContext context, string button, bool pressed, long durationMs)
        => input.Override(context, button, pressed, durationMs);
}

public sealed record ButtonOverrideReceipt(string LeaseId);
```

生成対象は public、非 generic、非 abstract、トップレベルの partial class。基底クラスと手書き Configure は使用しない。Operation は同期インスタンスメソッドで `DiagnosticResult<T>` を返す。context は先頭に置けるが、省略可能。残りの引数は bool / long / double / string に限定し、nullable、optional、params、ref、オーバーロードを拒否する。

T は public な非 generic record / class。public の読み取り可能な scalar プロパティを一度読んで detached snapshot に保持し、非 scalar はエラーにする。`IDiagnosticContributor.Configure` を明示実装するため、private メソッドを public に変える必要はない。

## 結果の直接書き込み

Generator は転送用 DTO や `Dictionary<string, DiagnosticValue>` を生成しない。戻り値の scalar だけを保持する `DiagnosticOutputValues` の実装と `WriteTo<TWriter>` を生成する。各値を `IDiagnosticValueWriter.Write(name, value)` へ渡し、HTTP / MessagePack の物理 Writer は通信モジュールが選ぶ。Writer は ref struct も扱え、ボックス化しない。ドメインオブジェクトやその getter を送信スレッドから参照しない。成功値は同じ経路の validating Writer で schema を検証する。

既存の `DiagnosticOperationResult.Values` は互換用 dictionary view として残る。indexer・列挙・Keys などを使うと初めて materialize するため、送信コードは `DiagnosticValueWriting.WriteTo` を使う。手動 Contributor の dictionary 出力も引き続き使える。

## 名前と任意の上書き

- `OverrideButton` → `override-button`、`durationMs` → `duration-ms`、`LeaseId` → `lease-id`、`GetURLValue` → `get-url-value` と推論する。
- `[DiagnosticOperation(Id = "stable-id", DisplayName = "表示名", RequiresRevision = true)]` で安定 ID、表示名、リビジョン必須を指定する。
- `[DiagnosticArgument("stable-field", Minimum = 1, Maximum = 5000)]` で引数 ID・数値範囲を指定する。String は MaxLength を指定できる。
- `[property: DiagnosticMember("lease-id")]` で positional record の結果プロパティ ID を指定し、`[property: DiagnosticIgnore]` で結果から除外する。

命名を変えると推論 ID も変わる。外部クライアントとの互換性を固定する段階で Id と SchemaVersion を管理する。未注釈のメソッドは公開しない。bool / long / double / string の scalar 以外の型、無効な範囲、重複 ID はコンパイル時に拒否する。

## 診断コード

| コード | 拒否する宣言 |
| --- | --- |
| LMDIAG001 | Contributor クラスの形・手書き Configure の衝突 |
| LMDIAG002 | Operation メソッド・戻り値の形 |
| LMDIAG003 | ID の重複、空 ID、オーバーロード |
| LMDIAG004 | 未対応・nullable の引数／結果プロパティ |
| LMDIAG005 | 権限、値域、長さ制約の不正 |

テストは Roslyn の実コンパイルと生成結果を確認し、Input サンプルの生成コードを実際に DI / Pump から実行する。生成コードは reflection を使わないが、NativeAOT / Browser のビルド検証はまだ行っていない。
