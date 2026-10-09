# Lumyte.Settings

.NET 10 の Options と標準バリデータを利用し、複数モジュールの設定を一つの JSON ドキュメントに保存します。

```csharp
var source = new PersistedJsonFileSource(settingsPath);
builder.Configuration.AddPersistedJsonFile(source);
builder.Services.AddSettings(source);
builder.Services.UseMyModule(); // モジュール側で設定を登録する API の例。
```

`settingsPath` は絶対パスです。ファイルは構成のロード時に同期読み込みされます。モジュール登録と共通基盤の DI 登録順は問いません。Host の起動時に未参照モジュールも自動検証します。Host を使わない Engine は、起動完了前に `ISettingsDocument.ValidateRegisteredSettings()` を呼びます。

モジュールは次の登録を内部で行います。標準 `Configure`、`PostConfigure`、`Validate`、`IValidateOptions<T>` を利用できます。

```csharp
services.AddPersistedOptions<MySettings>("my-module")
    .Configure(value => value.Volume = 0.5f)
    .Validate(value => float.IsFinite(value.Volume), "Volume must be finite.")
    .UseJsonTypeInfo(MySettingsJsonContext.Default.MySettings);
```

通常は `ISettingsDefinition<T>` を作りません。`UseJsonTypeInfo` には標準のソースジェネレーターが生成した型情報を渡します。

```csharp
[JsonSerializable(typeof(MySettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class MySettingsJsonContext : JsonSerializerContext;
```

AOT / trimming を使わない環境では型情報の指定も省略でき、`AddPersistedOptions<MySettings>("my-module")` だけで camelCase のリフレクション型情報を利用します。リフレクション無効時は明示した型情報が必要です。

共通基盤が JSON 型情報の getter / setter と生成ファクトリーを使い、検証前に JSON 化せず深いコピーを作ります。有限でない数値や null も保持します。ルートの設定モデルは引数なしで生成できる class に限定し、コレクション型は登録時に拒否します。プロパティには、読み書き可能な JSON オブジェクト、標準のスカラー値、その nullable、一次元配列、`List<T>`、`Dictionary<string, T>` を使用できます。辞書と配列は既定値へ追加せず全体を置き換えます。コピー時には辞書の比較方法を保持し、保存値の読み込みでは設定した既定辞書の比較方法を引き継ぎます。JSON から除外する状態、読み取り専用メンバー、独自コンバーター、ポリモーフィズムやその他の型には `UseJsonDefinition<T, TDefinition>` で独自の定義を指定します。明示した型情報の対応外モデルは登録時に検出します。省略時は定義の初回解決時に検出します。

保存形式のバージョンは既定で 1 です。旧形式への対応が必要な場合だけ移行関数を指定します。現在のバージョンでは関数を呼びません。移行は読み込み時にメモリ上で行い、保存するまで元データを書き換えません。

```csharp
services.AddPersistedOptions<MySettings>("my-module")
    .UseJsonTypeInfo(MySettingsJsonContext.Default.MySettings,
        schemaVersion: 2,
        upgrade: (values, oldVersion) => MigrateToVersion2(values, oldVersion));
```

旧版の移行関数が未登録の場合はそのセクションを保護し、復旧まで上書きしません。独自定義を使う場合は JSON 型情報、形式移行、検証前の深いコピーをモジュール側で提供します。

```csharp
var settings = provider.GetRequiredService<IEditableOptions<MySettings>>();
var edit = settings.BeginEdit();
edit.Value.Volume = 0.8f;
var result = await settings.SaveAsync(edit, cancellationToken);
```

保存 JSON にキーがない場合は既定値を使用します。明示的な `0`、`false`、`null` は省略せず保存します。渡した型情報の省略設定や属性は永続化用の型情報で調整し、元の JSON オプションは変更しません。JSON の必須キー指定も既定値の補完に合わせて調整し、設定値自体の必須条件は標準 Options バリデータで検証します。未知のプロパティは階層を問わず無視して読み込み、そのモジュールを次に保存したときに削除します。

保存前に、候補を読み込み経路で復元・検証し、再シリアライズした保存値が一致することを確認します。検証のため Configure / PostConfigure / バリデータも実行されるので、副作用のない処理を登録してください。文書全体の最大深度は外側の構造を含めて 64 とし、読めない出力は `ValidationFailed` で拒否します。

保存成功後だけ現在値と対象モジュールの Revision を更新します。古い編集は `Conflict`、不正値は `ValidationFailed`、保存媒体のエラーは `StorageFailure` です。別モジュールの更新と未登録セクションは保持します。全体保存を直列化し、保存候補は呼び出し時にコピーします。コピー中に同じ edit を別スレッドから変更しないでください。

取得する値は常に内部状態から独立したコピーです。リセットと PostConfigure 後にも参照を分離し、構成コールバックが渡したリストを後から変更しても確定値は変化しません。`IOptions<T>` の初期値は保存済み設定を含みますが、標準のキャッシュ契約により保存後は更新されません。実行中の確定値は `IEditableOptions<T>` を使用します。

セクションの破損はそのモジュールの `ResetAsync(revision)` で復元できます。文書全体の破損では個別保存・個別リセットを拒否します。明示的な `ISettingsDocument.ResetAsync()` は登録済みの既定値で文書を再作成し、未登録セクションも削除します。UI はこの影響を説明してください。読み込み失敗時に自動で元ファイルを上書きしません。

Browser など非同期ストアは `ISettingsStore` を実装し、構成登録前に読み込みます。

```csharp
var source = await PersistedSettingsSource.LoadAsync(store, cancellationToken);
builder.Configuration.AddPersistedSettings(source);
builder.Services.AddSettings(source);
builder.Services.UseMyModule(); // モジュール側で設定を登録する API の例。
```

ストアは借用し、構成側がリソースを所有します。書き込み失敗・コミット前のキャンセルでは旧データを保持し、コミット後はキャンセルで失敗を報告しない契約です。外部編集の監視や複数プロセス・タブ間の競合制御は提供しません。

設計判断は [ADR-SETTINGS-0001](../../../docs/adr/settings/SETTINGS-0001-user-settings-persistence.md) を参照してください。
