# Lumyte.Diagnostics.Settings

設定の公開項目をDIで選び、既存の診断通信から参照・保存します。
Settings本体とゲーム側アダプターはHTTP、MagicOnion、Blazorに依存しません。

```csharp
builder.Configuration.AddPersistedSettings(source); // 非同期媒体は先にLoadAsyncで読み込む。
services.AddSettings(source);
services.AddPersistedOptions<AudioSettings>("audio")
    .Validate(value => value.Volume is >= 0 and <= 1, "Volume is out of range.")
    .UseJsonTypeInfo(AudioJsonContext.Default.AudioSettings);
services.AddDiagnosticExecutionPoint<BeforeUpdate>("before-update");
services.AddSettingsDiagnostics<AudioSettings, BeforeUpdate>("audio", fields => fields
    .Double("volume", value => value.Volume, (value, volume) => value.Volume = volume)
    .Boolean("muted", value => value.Muted, (value, muted) => value.Muted = muted)
    .String("device", value => value.Device)); // setterなしは読み取り専用。
```

追加属性は不要です。`IEditableOptions<T>`と診断の実行ポイントは既存のDI登録を使用します。
モデルごとに一度登録し、公開項目は最大64件、文字列は最大4096文字です。
getter／setterは副作用を持たず、getterは正常な有限値を返す必要があります。
未登録プロパティと秘密は診断に送られず、保存時も保持されます。

診断サブシステムは`settings.audio`、操作は`read`、`save`、`save-result`です。
全項目が読み取り専用なら`read`だけを公開します。

1. `read`で値、初回`load-status`、Revisionを取得する（Observe権限）。
2. `save`へ全編集可能項目とExpectedRevisionを送る（Edit権限）。
3. 戻り値の`job-id`を`save-result`へ渡す（Observe権限）。
4. `write-status`が`Saved`になったら、`read`で確定値を確認する。

`save`の成功応答は保存開始の受付です。保存完了を意味しません。
結果は`Pending`、`Saved`、`Conflict`、`ValidationFailed`、`StorageFailure`、
`RecoveryRequired`、`Cancelled`、`Failed`です。Revisionは結果とともに返します。
保存中の別の開始は`busy`。開始前の古いRevisionは`conflict`、Revisionなしは
`revision-required`となります。最新の保存結果一件だけを保持し、古いjob-idや
別セッションの照会は`not-found`です。操作を自動再送しないでください。

保存候補のコピー・検証はゲームの実行ポイントで行います。
非同期I/Oの完了をゲームループで待ちません。接続終了またはスコープ破棄で
未コミットの保存を取り消し、コミット後はSettingsの成功契約を守ります。
読み取り結果は独立したスナップショットから直接writerへ出力します。
エラーの原文、保存先パス、設定全体のJSONは公開しません。

サーバーのSettingsページは該当カタログの操作を表示し、操作結果をInspectorに表示します。
設計判断は[ADR-SETTINGS-0002](../../../docs/adr/settings/SETTINGS-0002-diagnostic-operations.md)を参照してください。
