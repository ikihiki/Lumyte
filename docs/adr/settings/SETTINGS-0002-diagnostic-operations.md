# ADR-SETTINGS-0002: 設定の診断公開と非同期保存

- 状態: 採用
- 日付: 2026-10-09
- 更新日: 2026-10-11

## 背景

[IEditableOptions の永続化契約](SETTINGS-0001-user-settings-persistence.md)を診断サーバーから利用する。既存の診断操作はゲームの実行ポイントで同期実行される一方、設定の保存媒体は非同期であり、保存完了を同期で待つとゲームループを止める。秘密や内部設定を自動公開せず、既存の検証・コピー・Revision 競合検出を維持する必要がある。

## 決定

独立した `Lumyte.Diagnostics.Settings` パッケージを追加し、Settings と通信非依存の Diagnostics に依存する。Settings 本体に診断や通信層への依存を追加しない。`AddSettingsDiagnostics` の登録を設定モデル全体の診断公開に対する明示的なオプトインとする。属性や実行時リフレクションは使わず、`UseJsonTypeInfo` で登録済みの source-generated `JsonTypeInfo<T>` を読み書きにも再利用する。既存の bool、long、double、string の個別フィールド定義は汎用操作との互換性のため維持する。

### 公開 API

比較元: origin/main（5bf75e4）。以下は追加 API。

```diff
+namespace Lumyte.Diagnostics.Settings
+{
+    public sealed class SettingsDiagnosticFields<T> where T : class, new()
+    {
+        // setter が null なら読み取り専用。ID は重複不可、load-status は予約。
+        public SettingsDiagnosticFields<T> Boolean(string id, Func<T, bool> get, Action<T, bool>? set = null);
+        public SettingsDiagnosticFields<T> Int64(string id, Func<T, long> get, Action<T, long>? set = null);
+        public SettingsDiagnosticFields<T> Double(string id, Func<T, double> get, Action<T, double>? set = null);
+        // maxLength は既定 4096。getter の値も公開スキーマで検証する。
+        public SettingsDiagnosticFields<T> String(string id, Func<T, string> get, Action<T, string>? set = null, int maxLength = 4096);
+    }
+    public static class SettingsDiagnosticsExtensions
+    {
+        // scoped なアダプター。既存 IEditableOptions<T> を借用し、保存は同じサービスへ委譲。
+        public static IServiceCollection AddSettingsDiagnostics<T, TPoint>(
+            this IServiceCollection services, string moduleId,
+            Action<SettingsDiagnosticFields<T>> configure)
+            where T : class, new() where TPoint : class;
+    }
+}
```

登録時に設定の公開定義をコピーし、以後の変更を拒否する。診断サブシステム ID は `settings.{moduleId}`。公開操作は次の契約とする。

- `read-tree`: Observe 権限。設定モデル全体を最大 1 MiB の JSON document、初回 LoadStatus、Revision として返す。JSON は source-generated metadata で生成し、診断出力 writer へ直接書く。
- `save-tree`: Edit 権限と ExpectedRevision が必須。最大 1 MiB の JSON document を同じ metadata で設定型へ復元し、detached な編集候補全体を置換する。不正 JSON は `invalid-document` とし、既存の検証、Revision 競合、永続化を迂回しない。戻り値は `save` と同じ job-id とする。
- `read`: Observe 権限。公開値と初回 LoadStatus、スナップショットの Revision を返す。JSON 全体や保存パス、例外の原文は返さない。出力は独立した SettingsSnapshot を保持し、値を直接 writer へ書く。
- `save`: Edit 権限と ExpectedRevision が必須。全ての編集可能項目を指定し、BeginEdit で取得した現在値の公開項目だけを変更する。Revision が不一致なら開始せず Conflict を返す。媒体の保存成功まで現在値を変更しない。戻り値の job-id は保存開始を表し、保存成功を意味しない。
- `save-result`: Observe 権限。job-id を指定し、Pending、Saved、Conflict、ValidationFailed、StorageFailure、RecoveryRequired、Cancelled、Failed と確定 Revision を確認する。検証メッセージや例外の原文は非公開項目を含み得るため転送しない。

同一モジュール・ゲームスコープで `save` と `save-tree` を合わせて保存は一件まで。進行中の再開始は busy、最新の保存結果一件だけを保持する。古い job-id は not-found。操作は自動再送しない。セッション終了の取消とアダプター破棄で未コミットの保存を取消す。コミット後の成功は既存の保存契約に従う。個別の編集可能フィールドがなくても `save-tree` と `save-result` は公開する。

setter は診断の所有スレッドで編集候補だけに作用する。SaveAsync は候補を同期コピーしてから非同期 I/O を開始するため、コピー・検証の時間は実行ポイントの予算に含まれる。Task.Run や同期 wait による保存待機は行わない。正常で有限な公開値と、副作用のない getter／setter を登録側の契約とする。

診断サーバーの Settings ページは最上部に設定システムのログ一覧を折りたたんで置き、その下に再読み込み・保存ツールバーと設定ツリーだけを表示する。ツリーは object、array、dictionary、null、scalar を省略せず再帰表示する。保存は画面が保持する Revision で `save-tree` を開始し、job-id を自動でポーリングして Saved を確認後に再読み込みする。HTTP と MagicOnion のゲーム接続で同じ API を使う。

## 検討した代替案

JSON document を診断用の独自 object DTO へ展開する方式は、型ごとの DTO と MessagePack / HTTP の二重実装が必要になる。JSON は既存の永続化メタデータと同じプロパティ名・型を保ち、DTO を作らず直接 writer の string field へ格納できるため採用した。登録自体をモデル全体の公開境界とし、操作には既存の Observe / Edit 認可を適用する。保存完了を同期で待つ方式はゲームループを止めるため採用しない。

## 結果と影響

診断公開をモデル単位でオプトインにでき、ゲーム側は通信方式を意識しない。登録したモデルの全プロパティが Observe 権限を持つ利用者へ送られ、Edit 権限を持つ利用者が編集できるため、秘密を含む型は登録しないか診断専用モデルへ分ける。保存は二段階だが UI が結果確認を行う。競合・検証・保存失敗は既存サービスで判定する。個別モジュールのリセットと変更通知の自動購読は対象外とする。

## 検証方針

個別公開項目の互換性、全体 JSON の入れ子・配列、不正 JSON、権限、Revision 必須・競合、検証失敗、媒体失敗、保存進行中の busy と取消を確認する。実サーバーと両通信方式で read-tree → save-tree → save-result → read-tree を行い、保存後の値と Revision を照合する。ブラウザーではログの初期折りたたみ、全階層の表示、再読み込み、保存、狭い画面を確認する。

### 標準 .NET テレメトリー

Settings 本体で Meter／ActivitySource／ILogger を生成し、診断経由以外の保存・リセットも計測する。Meter と ActivitySource、ログカテゴリの名前は `Lumyte.Settings`。Meter は DI の IMeterFactory が所有し、ActivitySource は DI の singleton 計測サービスが所有する。Settings は引き続き診断や通信層へ依存しない。

```diff
+namespace Lumyte.Settings
+{
+    public static class SettingsTelemetry
+    {
+        public const string MeterName = "Lumyte.Settings";
+        public const string ActivitySourceName = "Lumyte.Settings";
+        public const string LogCategoryName = "Lumyte.Settings";
+        // 任意のゲーム相関。AsyncLocal で伝播し、Dispose で直前のスコープへ戻す。
+        public static IDisposable BeginScope(Guid instanceId);
+    }
+}
```

- `settings.operations`: 終了した処理数。operation、section、status をタグにする。
- `settings.operation.duration`: 処理時間（ms）。save／reset は候補のコピー・検証・待機・保存・コミットを含む。store はシリアライズと媒体書き込みを含む。
- `settings.write.wait.duration`: 書き込みの排他待ち時間（ms）。待機結果は Acquired／Cancelled／Failed。
- `settings.writes.active`: 実行中の保存・リセット数。開始と終了で同じタグに +1／-1 を記録する。

処理区分は document-load、load、save、reset、document-reset、store。Snapshot 参照や診断の結果ポーリングは計測しない。初回 document-load の媒体読み込みは構成／DI の起動前に行うため、所要時間を source に保持し、DI 解決時に結果のメトリクスとログを一度発行する。実際の初期化・移行・検証の load、非同期保存・リセット・store は実行中の Span を生成する。

Span のタグと構造化ログには operation、section、status と確定 Revision を記録できる。メトリクスへ Revision、Request ID、job-id を付けない。設定値や JSON、保存パスを専用フィールドで出力しない。発生した例外は ILogger の Exception 引数で記録し、Span に標準の exception イベントと exception.type／exception.message／exception.stacktrace タグを付与する。例外の原文にはパスや検証内容を含む場合があるため、診断操作の応答とは分け、認証済みのテレメトリーで原因を確認する。診断転送の文字列は既存の 4096 文字上限を適用し、標準 ILogger には元の例外を渡す。結果へ変換する例外、初回物理ロードで保持した例外、予期せず伝播する例外を捕捉し、再送出は throw で元の例外とスタックを保持する。保存失敗・無効なデータなどは Error Span、Conflict／RecoveryRequired は警告ログ、Cancelled は通常の取消結果とする。計測先の例外で保存の成功・失敗を変えない。

SettingsTelemetry.BeginScope を利用した場合だけ `lumyte.instance.id` を付ける。診断アダプターは登録時の解決と read／save をこのスコープに入れ、非同期完了まで維持する。ゲームの診断収集で Meter／ActivitySource／ログカテゴリを明示許可する。ゲーム相関がない起動時の記録も、標準の .NET／OpenTelemetry の購読では観測できる。診断だけの busy や開始前の Revision 拒否は永続化の試行数に含めない。
