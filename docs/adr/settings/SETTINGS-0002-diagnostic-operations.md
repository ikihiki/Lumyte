# ADR-SETTINGS-0002: 設定の診断公開と非同期保存

- 状態: 採用
- 日付: 2026-10-09

## 背景

[IEditableOptions の永続化契約](SETTINGS-0001-user-settings-persistence.md)を診断サーバーから利用する。既存の診断操作はゲームの実行ポイントで同期実行される一方、設定の保存媒体は非同期であり、保存完了を同期で待つとゲームループを止める。秘密や内部設定を自動公開せず、既存の検証・コピー・Revision 競合検出を維持する必要がある。

## 決定

独立した `Lumyte.Diagnostics.Settings` パッケージを追加し、Settings と通信非依存の Diagnostics に依存する。Settings 本体に診断や通信層への依存を追加しない。属性やリフレクションによる全プロパティの公開を行わず、DI の登録で公開する bool、long、double、string の getter と任意の setter を指定する。ネストしたモデルも getter／setter で明示できる。nullable・コレクション全体の編集は対象外とする。

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

- `read`: Observe 権限。公開値と初回 LoadStatus、スナップショットの Revision を返す。JSON 全体や保存パス、例外の原文は返さない。出力は独立した SettingsSnapshot を保持し、値を直接 writer へ書く。
- `save`: Edit 権限と ExpectedRevision が必須。全ての編集可能項目を指定し、BeginEdit で取得した現在値の公開項目だけを変更する。Revision が不一致なら開始せず Conflict を返す。媒体の保存成功まで現在値を変更しない。戻り値の job-id は保存開始を表し、保存成功を意味しない。
- `save-result`: Observe 権限。job-id を指定し、Pending、Saved、Conflict、ValidationFailed、StorageFailure、RecoveryRequired、Cancelled、Failed と確定 Revision を確認する。検証メッセージや例外の原文は非公開項目を含み得るため転送しない。

同一モジュール・ゲームスコープで保存は一件まで。進行中の再開始は busy、最新の保存結果一件だけを保持する。古い job-id は not-found。操作は自動再送しない。セッション終了の取消とアダプター破棄で未コミットの保存を取消す。コミット後の成功は既存の保存契約に従う。読み取り専用の公開では save と save-result を登録しない。

setter は診断の所有スレッドで編集候補だけに作用する。SaveAsync は候補を同期コピーしてから非同期 I/O を開始するため、コピー・検証の時間は実行ポイントの予算に含まれる。Task.Run や同期 wait による保存待機は行わない。正常で有限な公開値と、副作用のない getter／setter を登録側の契約とする。

診断サーバーに Settings ページを追加し、`settings.` 名前空間のカタログ操作を表示する。既存の操作フォームと Inspector を使い、読み取り結果の Revision で保存を開始し、job-id で結果を確認する。HTTP と MagicOnion のゲーム接続で同じ API を使う。

## 検討した代替案

全設定 JSON を転送する方式は、秘密や未公開項目を露出し、既存の scalar 操作スキーマを迂回する。保存完了を同期で待つ方式は、ゲームループの停止につながる。診断ディスパッチ全体を非同期に変える方式は、今回の機能に比べて実行ポイントの所有権・キャンセル契約への変更が大きい。明示した値と有界の保存ジョブを採用する。

## 結果と影響

診断公開をオプトインにでき、ゲーム側は通信方式を意識しない。保存は二段階になり、開始後の結果確認が必要になる。未公開設定を保持し、競合・検証・保存失敗を既存サービスで判定できる。設定全体や個別モジュールのリセットは未公開項目にも影響するため公開しない。詳細な専用設定フォームや変更通知の自動購読は後続の拡張とする。

## 検証方針

公開項目の限定、読み取り専用、権限、Revision 必須・競合、検証失敗、媒体失敗、保存進行中の busy と取消を確認する。実サーバーと両通信方式で read → save → save-result → read を行い、保存後の値と Revision を照合する。

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
