# Lumyte.Settings

.NET 10 の Options と標準バリデータを利用し、複数モジュールの設定を一つの JSON ドキュメントに保存します。

```csharp
var source = new PersistedJsonFileSource(settingsPath);
builder.Configuration.AddPersistedJsonFile(source);
builder.Services.AddSettings(source);
builder.Services.UseInput();
```

`settingsPath` は絶対パスです。ファイルは構成のロード時に同期読み込みされます。モジュール登録と共通基盤の DI 登録順は問いません。Host の起動時に未参照モジュールも自動検証します。Host を使わない Engine は、起動完了前に `ISettingsDocument.ValidateRegisteredSettings()` を呼びます。

モジュールは次の登録を内部で行います。標準 `Configure`、`PostConfigure`、`Validate`、`IValidateOptions<T>` を利用できます。

```csharp
services.AddPersistedOptions<MySettings>("my-module")
    .Configure(value => value.Volume = 0.5f)
    .Validate(value => float.IsFinite(value.Volume), "Volume must be finite.")
    .UseJsonDefinition<MySettings, MySettingsDefinition>();
```

設定定義は `ISettingsDefinition<T>` を実装し、JSON 型情報、形式移行、独立した深いコピーを提供します。コピーは不正な値も保持し、検証前に JSON 化しません。JSON ソースジェネレーターの型情報を使用でき、辞書と配列は既定値へ追加せず全体を置き換えます。

```csharp
var settings = provider.GetRequiredService<IEditableOptions<MySettings>>();
var edit = settings.BeginEdit();
edit.Value.Volume = 0.8f;
var result = await settings.SaveAsync(edit, cancellationToken);
```

保存成功後だけ現在値と対象モジュールの Revision を更新します。古い編集は `Conflict`、不正値は `ValidationFailed`、保存媒体のエラーは `StorageFailure` です。別モジュールの更新と未登録セクションは保持します。全体保存を直列化し、保存候補は呼び出し時にコピーします。コピー中に同じ edit を別スレッドから変更しないでください。

取得する値は常に内部状態から独立したコピーです。`IOptions<T>` の初期値は保存済み設定を含みますが、標準のキャッシュ契約により保存後は更新されません。実行中の確定値は `IEditableOptions<T>` を使用します。

セクションの破損はそのモジュールの `ResetAsync(revision)` で復元できます。文書全体の破損では個別保存・個別リセットを拒否します。明示的な `ISettingsDocument.ResetAsync()` は登録済みの既定値で文書を再作成し、未登録セクションも削除します。UI はこの影響を説明してください。読み込み失敗時に自動で元ファイルを上書きしません。

Browser など非同期ストアは `ISettingsStore` を実装し、構成登録前に読み込みます。

```csharp
var source = await PersistedSettingsSource.LoadAsync(store, cancellationToken);
builder.Configuration.AddPersistedSettings(source);
builder.Services.AddSettings(source);
builder.Services.UseInput();
```

ストアは借用し、構成側がリソースを所有します。書き込み失敗・コミット前のキャンセルでは旧データを保持し、コミット後はキャンセルで失敗を報告しない契約です。外部編集の監視や複数プロセス・タブ間の競合制御は提供しません。

設計判断は [ADR-SETTINGS-0001](../../../docs/adr/settings/SETTINGS-0001-user-settings-persistence.md) を参照してください。
