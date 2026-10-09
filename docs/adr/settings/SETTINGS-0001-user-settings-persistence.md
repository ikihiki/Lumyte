# ADR-SETTINGS-0001: ユーザー設定の読み込み・検証・保存の一元管理

- 状態: 採用
- 日付: 2026-10-09
- 関連カテゴリ: input、platform

## 背景

Lumyte では Input のリマッピングとデッドゾーンをユーザーが編集し、次回起動時にも復元できるようにする。読み込みだけでなく、検証、保存、既定値への復元、実行中の設定への反映を同じ機構で管理する必要がある。

`Microsoft.Extensions.Configuration` は複数の設定ソースを読み込むための基盤であり、プロバイダーの値を変更しても JSON ファイルなどへの永続化は保証されない。ユーザー設定の保存には別の契約が必要である。

対象環境は Windows、Linux、Browser とする。[ADR-INPUT-0001](../input/INPUT-0001-input-system.md) の InputSystem は正規化された入力の記録を管理し、管理スレッドから利用する。設定ファイルの I/O やアクション変換を InputSystem に追加せず、上位層で接続する。

## 決定

### .NET 標準機能の利用範囲

ユーザー設定は .NET の Options パターンを拡張して管理する。DI 登録には `OptionsBuilder<T>` を使用し、既定値を `Configure`、正規化を `PostConfigure`、型付き設定値の検証を `IValidateOptions<T>` で宣言する。独自のバリデータ基盤は作らない。保存データの読み込みは構成プロバイダー、形式移行・既定値の補完・原子的保存・編集競合・確定済み設定の公開は永続化サービスが担当する。JSON の読み書きには `System.Text.Json` を使用する。

`Microsoft.Extensions.Configuration` の構成ソースとして永続化設定を登録する。その他の起動時構成や環境変数も標準の構成基盤を使用する。ユーザーが編集する同じ項目に環境変数などの上書きを重ねず、保存した値と実際に使う値の対応を維持する。

本 ADR は設定管理の採用方針である。本 PR では共通ライブラリのみを実装し、Browser 固有ストアは注入可能な契約を提供する。Input モジュールの実装と共通基盤への接続は別の変更で扱う。アクションの評価・入力伝播の詳細は後続 ADR に分離する。

### 責務と依存関係

| 構成要素 | 責務 |
| --- | --- |
| `Lumyte.Settings` | 汎用サービス、結果型、JSON の読み書き、保存先の共通契約 |
| 標準 Options の登録 | `Configure` / `PostConfigure` による既定値・正規化、`IValidateOptions<T>` による検証 |
| 設定定義 `ISettingsDefinition<T>` | JSON 型情報、形式移行、検証前の深いコピー |
| 永続化設定ソース / プロバイダー | 構成読み込み時の保存データ取得、元 JSON と診断の保持 |
| 設定ストア `ISettingsStore` | 保存媒体への読み込みと全体置き換え |
| 共通ドキュメント管理 | 全セクションの保持、保存の直列化、全体復旧 |
| 各モジュールの有効化 API | 設定型、セクション ID、既定値、バリデータの自動登録 |
| アプリケーション / Engine の構成側 | 共通ソースの選択、設定画面、Input への橋渡し |
| Input の上位処理 | スナップショットからのリマッピング表生成とデッドゾーン変換 |

汎用ライブラリを `src/Core/Lumyte.Settings/` に配置する。`Lumyte.Settings` は `Lumyte.Input` や OS 固有 API に依存しない。Input 設定の型と検証は Input 側、アプリ独自のアクション定義はアプリ側に置く。Browser のストアは Platform 側で実装する。設定モデル・登録・変換処理を Input パッケージに配置する方針とし、本 PR には含めない。

### データモデルと互換性

共通設定ファイルは一つのドキュメントにモジュール別セクションを持つ。例えば `{ "documentVersion": 1, "sections": { "input": { "schemaVersion": 1, "values": {} } } }` とする。documentVersion は共通の外側形式、schemaVersion はモジュールごとの形式であり、個別に移行する。モジュールの保存は対象セクションの値全体を置き換え、媒体には最新のドキュメント全体を原子的に保存する。設定モデルは標準 Options と互換の引数なしコンストラクターを持つ class とする。UI は独立した編集コピーを持つ。保存呼び出し時に共通基盤または独自設定定義の `DeepClone` で候補を深く複製し、以後の UI 編集と分離する。コピーは JSON シリアライズを前提にせず、NaN・Infinity・不正な null なども値を変えずに保持して標準バリデータへ渡す。内部の確定済みモデルは外部に公開せず、公開スナップショットは内部コレクションまで深く複製する。取得したコピーを変更しても確定済み設定や他の利用者には影響しない。Input 側は取得したコピーから不変の変換表を構築する。

永続化サービスは JSON の項目の存在を確認して、新しく追加された未指定項目だけを既定値で補完する。明示的な `false`、`0`、空配列を未指定と扱わない。明示的な `null` の可否は JSON の型情報と標準バリデータで検証する。既存の保存値はアプリの既定値変更後も維持し、リセットで新しい既定値に戻す。

旧バージョンは JSON 上で順に移行し、補完、型付きモデルへの変換、検証を行う。読み込み時の移行だけでは元ファイルを書き換えない。未対応 documentVersion は全体の互換性エラー、モジュール内の未対応 schemaVersion や未知プロパティはそのセクションの互換性エラーとする。未登録モジュールのセクションは互換性エラーにせず、元 JSON のまま保持する。他モジュールの保存によって削除・移行しない。

形式の `schemaVersion` と、実行中の編集競合を検出する `Revision` は別である。Revision はモジュールごとの単調増加値で、ファイルには保存しない。

### 共通基盤とモジュール登録

アプリケーションは共通のソース・保存先を一度設定し、モジュールを有効化する。UseInput などのモジュール API が、設定型、安定したセクション ID、設定定義、既定値、標準バリデータを内部登録する。以下の UseInput はモジュール接続の設計例であり、本 PR では実装しない。具体的な公開 API は Input 側の実装で決定する。

```csharp
var source = new PersistedJsonFileSource(settingsPath);
builder.Configuration.AddPersistedJsonFile(source);

// 共通ソースを一度だけ選ぶ。
builder.Services.AddSettings(source);

// 設定の登録もモジュールに任せる。
builder.Services.UseInput();
```

AddSettings はソースの接続、共通ドキュメント管理、保存の直列化を登録する。モジュールは `AddPersistedOptions<T>`(sectionId) により記述子を登録し、標準 `OptionsBuilder<T>` を使って既定値や検証を宣言する。利用者がこの低水準 API を呼ぶ必要はない。

```csharp
// UseInput の内部で行う登録の例。
services.AddPersistedOptions<InputSettings>("input")
    .Configure(InputDefaults.Configure)
    .ValidateDataAnnotations()
    .UseJsonTypeInfo(InputSettingsJsonContext.Default.InputSettings);
services.AddSingleton<IValidateOptions<InputSettings>, InputSettingsValidator>();
```

AddPersistedOptions は ValidateOnStart を自動登録する。通常は型情報を指定するだけで共通基盤がコピーを提供し、SchemaVersion は 1 とする。AOT / trimming を使わない環境では型情報も省略でき、camelCase のリフレクション型情報を使う。旧版が必要な場合だけ UseJsonTypeInfo の schemaVersion と upgrade を指定し、現在版には移行関数を呼ばない。独自のコピー・直列化が必要な場合だけ ISettingsDefinition を実装する。モジュールは保存パスやストアの種類には依存しない。アプリ独自設定は同じ低水準 API で追加できる。利用者の既定値調整はモジュール有効化後の標準 `services.Configure<InputSettings>`(...) で行う。すべての Configure の後に保存値を適用するため、既存ユーザーの保存値は維持される。

共通基盤とモジュールの DI 登録順は問わず、解決時に接続する。全登録は DI 構築前に完了する。同一ソースの AddSettings は再登録しても重複処理しない。異なるソースの二重登録、同じセクション ID の別型への割り当て、同じ型の複数 ID への割り当て、共通基盤の未登録は構成エラーとする。UseInput などのモジュール有効化も既定値とバリデータを二重登録しない。

セクション ID は型名・表示名に依存しない英語の小文字・数字・ハイフンとし、モジュールの契約として固定する。セクション ID と Options の name は別で、初期対象は Options.DefaultName のみとする。プロファイルはモデル内で表す。

利用側は `IEditableOptions<InputSettings>` を注入し、BeginEdit、SaveAsync、ResetAsync を使う。ソースは事前ロード済みとし、DI 解決中に非同期 I/O を行わない。

自動コピーは JSON 型情報に含まれる getter / setter と生成ファクトリーを使う。引数なしで生成できる class、読み書き可能な JSON プロパティ、標準スカラーと nullable、一次元配列、`List<T>`、`Dictionary<string, T>` を対象とする。対応外の型・メンバーは明示登録時（型情報省略時は初回解決時）に構成エラーとする。読み取り専用状態、JSON から除外する状態、独自コンバーター、ポリモーフィズムは独自定義で扱う。

### 構成の読み込み時点と非同期

.NET 標準の `IConfigurationSource.Build`、`IConfigurationProvider.Load`、`IConfigurationBuilder.Build` は同期 API である。`AddJsonFile` は通常の ConfigurationBuilder ではソースを登録し、Build 時に Load が呼ばれる。ConfigurationManager ではソース追加時に Build / Load が呼ばれる。独自のファイルソースもこのタイミングで同期読み込みを完了する。

ファイルの取得と JSON の構文解析を Load で行い、元 JSON、保存先、読み込み診断をソースに保持する。この時点では DI に登録した型付きバリデータを実行しない。形式移行と型付き値の検証は、構成読み込み後の最初の Options 生成または永続化サービスの解決時に同期実行する。

.NET 標準の Options 生成・バインド・検証も同期であり、非同期の Configure や IValidateOptions は提供されない。ValidateOnStart は Host の起動処理から同期の生成・検証を呼び出すもので、非同期構成ロードへの変換ではない。IHostedService.StartAsync など Host のライフサイクルには非同期契約があるが、これは構成構築後の段階である。

Browser の IndexedDB など非同期読み込みが必要なストアでは、アプリの構成準備段階で次のように await する。これは独自 API であり、標準に BuildAsync / LoadAsync を追加するものではない。

```csharp
var source = await PersistedSettingsSource.LoadAsync(
    browserStore, cancellationToken);
builder.Configuration.AddPersistedSettings(source);

builder.Services.AddSettings(source);
builder.Services.UseInput();
```

取得済みソースの IConfigurationProvider.Load はメモリ内のデータを公開するだけで、再度非同期 I/O を行わない。GetAwaiter().GetResult、Result、Task.Run による同期化は使用しない。非同期準備の完了前に DI / Host を構築したり、Input を開始したりしない。AddPersistedOptions が自動登録する ValidateOnStart により、未参照の設定型も起動中に確定値の生成・検証を完了する。Generic Host を使わない場合は Engine が ISettingsDocument.ValidateRegisteredSettings を呼び、全登録済み設定の生成・検証後に起動完了を返す。

ソースは一つの構成ルートに登録し、共通ドキュメント管理を通じて複数の設定型へ接続する。DI 構築前にソースが読み込まれていることを確認し、未登録・未ロードなら構成エラーにする。外部ファイルの監視と IConfigurationRoot.Reload による再読み込みは初期対象外であり、取得済みデータを再公開する。保存成功時は元 JSON と構成プロバイダーのメモリ内表示も更新するが、変更通知による再読み込みは行わない。

### 標準バリデータの採用

型付きモデルの検証は DI に登録された `IEnumerable<``IValidateOptions<T>``>` を使用する。読み込み・保存・リセットのすべてで同じバリデータを実行し、`ValidateOptionsResult.Fail` の失敗文字列を集約する。既定の Options 名 `Options.DefaultName` を渡し、`Skip` は対象外として尊重する。バリデータは同期処理で、副作用を持たず、保存媒体への I/O を行わない。

単純な条件は `OptionsBuilder<T>.Validate`、属性の範囲・必須条件は `ValidateDataAnnotations`、デッドゾーンの有限値・項目間の大小関係や割り当ての衝突は `IValidateOptions<T>` で検証する。`Range` 属性だけでは `inner < outer` を表せない。DataAnnotations の通常のバリデータに任意のオブジェクトグラフの再帰検証を期待しない。ネストやコレクションを属性で検証する場合は標準の Options 検証ソースジェネレーターと `[ValidateObjectMembers]` / `[ValidateEnumeratedItems]` を使用する。

`ValidateOnStart` は標準 Options の生成を起動時に検証する機能であり、独自ストアからの読み込みや保存候補の検証を代行しない。最初の確定値生成と各書き込み操作で明示的に検証する。Browser / AOT では JSON と Options 検証のソースジェネレーターを活用し、反射を必須にしない登録方法を提供する。

`ValidateOptionsResult` の診断は文字列であり、標準契約に構造化された項目パス・エラーコードはない。初期設計では文字列をそのまま表示し、解析して項目パスを復元しない。JSON 形式、バージョン、I/O のエラーは永続化側の結果と区別する。

### 設定の構築順序

起動・リセットの既定値は、新しい T に名前が一致する `IConfigureOptions<T>` / `IConfigureNamedOptions<T>` を登録順に適用して作る。読み込みではこの既定値を JSON 化し、共通ドキュメントから取得した対象セクションの JSON を移行して、存在する項目を上書きする。型付きオブジェクトは既知のプロパティ単位で補完し、配列・辞書は存在する場合に全体置換する。この分類は JSON の見た目ではなく JsonTypeInfo のモデル種別に基づく。辞書の未指定は既定値、明示的な `{}` は空の辞書とし、保存済み辞書に存在しない既定のキーを補充しない。辞書キーは型付きプロパティの未知項目と区別し、許容するキー・各エントリの内容を標準バリデータで検証する。新しい既定エントリの追加が必要な場合は明示的な形式移行で行う。型付きオブジェクトの未知プロパティと不正な型を拒否する。その後、名前が一致する `IPostConfigureOptions<T>` を適用し、標準バリデータで検証する。

保存候補は現在の編集内容が全体を持つため、`Configure` を再適用してユーザー値を上書きしない。DeepClone で確保した独立の候補に `PostConfigure` を適用し、標準バリデータで検証してから保存用 JSON に変換する。NaN・Infinity は JSON 変換前に有限値バリデータで拒否して ValidationFailed を返す。検証を通った候補でも保存形式に表現できない場合は、その JSON 変換の診断を ValidationFailed として返し、ストアの書き込みと確定値の公開を行わない。正規化後の値を保存・公開する。`PostConfigure` は繰り返し適用しても結果が変わらない処理に限定する。

保存 JSON の適用はすべての既定値 Configure の後、PostConfigure の前に挿入する必要がある。設定型ごとの内部 singleton 状態が標準の登録済みインターフェースを使って上記の順序を組み立てる。`IOptionsFactory<T>` と編集サービスはこの一つの状態を共有し、初期ロード結果・診断・復旧保護・Revision を独立に生成しない。初期化は同期かつ排他的に一度だけ行い、不正な既定値や構成エラーによる失敗も保持して再実行しない。内部状態は `IOptions<T>`・`IOptionsMonitor<T>`・`IOptionsFactory<T>`・編集サービスに依存させず、これらが内部状態に依存する方向とし、循環依存を防ぐ。

本設定型には閉じた型の `IOptionsFactory<T>` を登録する。Create は共通状態の初期化を保証した後、現在の確定値の深いコピーを返す。ValidateOnStart は標準の `IOptionsMonitor<T>`.Get を通じてこの factory を呼ぶため、編集サービスが未参照でも同じ初期状態と診断が確定する。各キャッシュに返す T と編集用コピーは内部状態から分離し、利用者による変更が内部確定値へ伝わらないようにする。保存後の変更を反映する読み取り窓口は `IEditableOptions<T>` に統一する。`IOptions<T>` は最初の Value をキャッシュするため、保存後の変更を反映する読み取り窓口には使用しない。`IOptionsMonitor<T>` への保存時通知は初期対象外とする。

### 公開 API 案

比較元は `main` の `f69072fdf6089c006ac7a1c8af4712dee1b43579`。同 revision に `Lumyte.Settings` は存在しない。宣言は主要メンバーの抜粋である。`SettingsService<T>` は内部実装とし、標準 DI に singleton として登録する。

```diff
+namespace Lumyte.Settings
+{
+    // Value は内部確定値と分離した深いコピー。Revision はサービス内の版。
+    public sealed record `SettingsSnapshot<T>`(long Revision, T Value) where T : class, new();
+    // Value は編集可能。BaseRevision と生成元サービスの識別を保持する。
+    public sealed class `SettingsEdit<T>` where T : class, new()
+    {
+        public long BaseRevision { get; }
+        public T Value { get; }
+    }
+    public enum SettingsLoadStatus
+    {
+        Loaded, Defaults, InvalidData, UnsupportedVersion, StorageFailure
+    }
+    public enum SettingsSaveStatus
+    {
+        Saved, ValidationFailed, Conflict, StorageFailure, RecoveryRequired
+    }
+    // Errors は標準バリデータまたは形式・I/O の診断文字列。
+    public sealed record SettingsLoadResult(
+        SettingsLoadStatus Status, ImmutableArray<string> Errors);
+    public sealed record `SettingsSaveResult<T>`(
+        SettingsSaveStatus Status, `SettingsSnapshot<T>` Snapshot,
+        ImmutableArray<string> Errors) where T : class, new();
+    // 高度なカスタマイズ用。通常のモジュールでは実装不要。
+    public interface `ISettingsDefinition<T>` where T : class, new()
+    {
+        int SchemaVersion { get; }
+        `JsonTypeInfo<T>` JsonTypeInfo { get; }
+        // 入力を変更せず、内部コレクションまで独立したコピーを返す。
+        // 不正値・null・NaN・Infinity も保持し、検証前に JSON 化しない。
+        T DeepClone(T value);
+        // JSON 形式の移行のみ。型付き検証は `IValidateOptions<T>` に委譲する。
+        // 入力を変更せず、不正形式は JsonException、未対応版は NotSupportedException。
+        JsonObject Upgrade(JsonObject values, int sourceVersion);
+    }
+    public interface ISettingsStore
+    {
+        // null は保存データ不在。読み込み失敗は IOException。
+        ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default);
+        // 成功は媒体へのコミット完了。失敗時は旧データを維持する。
+        ValueTask WriteAtomicallyAsync(
+            ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
+    }
+    public sealed class JsonFileSettingsStore : ISettingsStore
+    {
+        // Windows / Linux 向け。絶対パスを構成側から渡す。
+        public JsonFileSettingsStore(string path);
+        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default);
+        public ValueTask WriteAtomicallyAsync(
+            ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
+    }
+    public interface `IEditableOptions<T>` where T : class, new()
+    {
+        // 解決時に取得済みデータから確定値を生成。診断を UI / ログへ渡す。
+        SettingsLoadResult LoadResult { get; }
+        // 任意スレッドから一貫して取得。不正な既定値は OptionsValidationException。
+        `SettingsSnapshot<T>` Current { get; }
+        // コピーなしの変更検出用。Current の Revision を最終的な基準とする。
+        long Revision { get; }
+        `SettingsEdit<T>` BeginEdit();
+        // 検証 → Revision 確認 → 保存 → 確定値置換。別サービスの edit は引数エラー。
+        Task<`SettingsSaveResult<T>`> SaveAsync(
+            `SettingsEdit<T>` edit, CancellationToken cancellationToken = default);
+        // 対象セクションを既定値に復元。全体破損時は RecoveryRequired。
+        Task<`SettingsSaveResult<T>`> ResetAsync(
+            long expectedRevision, CancellationToken cancellationToken = default);
+    }
+    public sealed record SettingsDocumentSaveResult(
+        SettingsSaveStatus Status, ImmutableArray<string> Errors);
+    public interface ISettingsDocument
+    {
+        // 非 Host の Engine 起動から使用。全モジュールを生成・検証。失敗は例外。
+        void ValidateRegisteredSettings();
+        // 文書全体の明示復元。未登録モジュールの保存値も失われる。
+        Task<SettingsDocumentSaveResult> ResetAsync(
+            CancellationToken cancellationToken = default);
+    }
+    // provider は IConfigurationProvider の同期 Load 契約を実装する。
+    // flatten した構成表示と別に元 JSON を保持し、null / 空配列 / 未指定を区別する。
+    public abstract class PersistedSettingsSource : IConfigurationSource
+    {
+        public IConfigurationProvider Build(IConfigurationBuilder builder);
+        // 非同期ストアの事前読み込み。キャンセルは OperationCanceledException。
+        // データ障害は診断付きソースを返す。注入ストアは構成側が所有する。
+        public static Task<PersistedSettingsSource> LoadAsync(
+            ISettingsStore store, CancellationToken cancellationToken = default);
+    }
+    public sealed class PersistedJsonFileSource : PersistedSettingsSource
+    {
+        // provider.Load で同期ファイル読み込み。型付き検証は後段で実行する。
+        public PersistedJsonFileSource(string path);
+    }
+    public static class PersistedOptionsExtensions
+    {
+        // モジュールの登録用。既定名のみ、ValidateOnStart を自動登録する。
+        public static `OptionsBuilder<T>` `AddPersistedOptions<T>`(
+            this IServiceCollection services, string sectionId) where T : class, new();
+        // アプリが共通ソースを一度接続。同一ソースの再登録は idempotent。
+        public static IServiceCollection AddSettings(
+            this IServiceCollection services, PersistedSettingsSource source);
+        // 通常の builder では Build 時、ConfigurationManager では追加時に同期ロード。
+        public static IConfigurationBuilder AddPersistedJsonFile(
+            this IConfigurationBuilder builder, PersistedJsonFileSource source);
+        // 標準モデルのコピーは共通基盤が提供。旧版対応時だけ移行を指定。
+        public static OptionsBuilder<T> UseJsonTypeInfo<T>(
+            this OptionsBuilder<T> builder, JsonTypeInfo<T> metadata,
+            int schemaVersion = 1, Func<JsonObject, int, JsonObject>? upgrade = null)
+            where T : class, new();
+        public static `OptionsBuilder<T>` UseJsonDefinition<T, TDefinition>(
+            this `OptionsBuilder<T>` builder)
+            where T : class, new() where TDefinition : class, `ISettingsDefinition<T>`;
+    }
+}
```

### 読み込み・編集・保存の契約

構成ロード時に保存データを取得し、標準 Options の最初の生成または `IEditableOptions<T>` の解決で共通状態を同期初期化する。LoadResult の診断を UI やログへ渡す。ファイル不在なら既定値で開始し、暗黙にファイルを作成しない。セクションの不正データ・未対応版では対象モジュールを診断付き既定値で開始し、その SaveAsync を RecoveryRequired で拒否する。当該モジュールの ResetAsync はそのセクションだけを既定値へ復元する。他モジュールの保存では不正なセクションを元 JSON のまま保持できる。

ドキュメント全体の JSON 構文破損、未対応 documentVersion、読み込み障害では、全モジュールを診断付き既定値で開始し、通常保存とモジュール単位のリセットを RecoveryRequired で拒否する。明示的な ISettingsDocument.ResetAsync だけを許可し、登録済み全モジュールの既定値を検証してドキュメント全体を再作成する。成功時だけ保護を解除し、全モジュールの Revision を進める。この全体復元では未登録モジュールの値も失われるため、利用側は個別リセットと区別して影響を提示する。

UI は BeginEdit で一貫した BaseRevision と編集コピーを取得する。DeepClone の実行中は同じ編集コピーを別スレッドから変更しない。SaveAsync が候補を確保した後の UI 編集は保存内容に影響しない。保存は全設定を一括で検証し、失敗なら標準バリデータの失敗文字列を返す。共通ドキュメント管理の保存ロック内で対象モジュールの Revision を確認し、古い編集コピーなら Conflict を返す。そのロック内で最新ドキュメントの対象セクションだけを置き換え、全体を原子的に保存する。別モジュールの変更を古いドキュメントコピーで上書きしない。成功時は最新ドキュメントの確定コピーを更新し、対象モジュールの Revision だけを進める。他モジュールの編集コピーは有効なままとする。成功時だけ保存内容と同じスナップショットを公開し、Revision を増やす。保存失敗では Current と Revision を維持し、UI の編集コピーから再試行できる。

内部の確定値全体を一度に置き換え、取得済みのコピーを変更しない。通知コールバックを保存処理に組み込まず、利用側がフレーム開始時などに Revision の変化を検出する。

引数違反と設定定義のプログラミングエラーは例外とする。キャンセルはコミット前なら `OperationCanceledException` とし、設定を変更しない。ストアはコミット開始直前に最後のキャンセル判定を行い、以降は結果を確定させる。コミット成功後は Current の公開まで完了し、キャンセルを保存失敗として報告しない。

一つのストアは共通ドキュメント管理が借用し、モジュールのサービスはストアへ直接書き込まない。Revision は同じモジュール内の編集競合を防ぐもので、複数プロセス、複数 Browser タブ、外部エディターとの競合検出や自動リロードは初期対象に含めない。注入ストアのリソースは構成側が所有し、保存完了を待ってから解放する。

### 保存媒体と障害時の扱い

Windows / Linux は構成側が選んだユーザー単位の設定ディレクトリを使用する。`JsonFileSettingsStore` は保存先と同じディレクトリの一時ファイルに書き込み、フラッシュ・クローズ後に置き換える。対応するファイルシステムで原子的な置き換えを使用し、それを保証できない保存先は失敗として報告する。失敗時に保存先を先に削除する実装は行わない。電源断に対する耐久性は OS とファイルシステムに依存し、原子的な可視性と区別する。

Browser はファイルパスを前提にせず、Platform が IndexedDB のトランザクションで置き換えるストアを注入する。トランザクションの完了を保存成功とし、容量不足、利用不可、トランザクション失敗を `StorageFailure` として報告する。保存できない状態を成功扱いしてメモリだけで確定しない。サイトデータの消去やブラウザーによる退避解除に対する永続性は保証しない。

### Input 設定との接続方針（別実装）

アクションは表示名と分離した安定した文字列 ID で識別し、コンテキスト単位で複数の物理入力を割り当てられるようにする。各アクションの割り当て配列は全体置換し、未指定は既定値、空配列は明示的な解除とする。キーやコントローラーの識別子は安定した文字列表現で保存し、enum の数値や実行中だけ有効な `InputDeviceId` は永続化しない。

デバイス種別・プロファイルを設定の単位とする。現行 InputDeviceInfo は再接続後も安定する物理 ID を持たないため、個体別設定は後続のデバイス識別設計まで提供しない。入力の重複はコンテキストとアクション定義の規則で検証する。

デッドゾーンは有限値で `0 <= inner < outer <= 1` を満たすものとする。スティックは長さ r を使う放射状の変換を基本とし、出力の長さを `clamp((r - inner) / (outer - inner), 0, 1)` とする。r が 0 の場合はゼロを返し、方向は維持する。トリガーは正規化済みの 0〜1 の値に同じしきい値変換を行う。既定 Inner はスティック 0.15、トリガー 0.05、Outer は 1 とする。

InputSystem の入力記録・現在状態は元の正規化値を保持する。デッドゾーンとリマッピングは記録の読み取り後に上位層で処理する。構成側は管理スレッドのフレーム開始時に Revision を確認し、変更時だけ Current の深いコピーを取得して、変更された Revision に対して変換表をまとめて切り替える。サービスの保存成功は確定済み設定の公開を意味し、Input への適用は次のフレーム境界で完了する。

Input の `IValidateOptions<T>` は変換表を構築できることまで保存前に検証する。即時プレビューは UI の編集コピーから一時的な変換表を作り、保存せず試す。キャンセル時は最新の確定済みスナップショットへ戻す。保存中に編集が続いた場合、保存候補以降の編集を未保存として維持する。

## 検討した代替案

### Microsoft.Extensions.Configuration を直接使用する

読み込みと複数ソースの統合には適しているが、編集・保存の保証がなく、アクション配列の統合も全体置換の規則と一致しない。読み込み基盤として採用し、元 JSON の保持と保存機構を追加する。

### IConfiguration 全体を独自ラッパーで包む

標準機能の読み込み API を再公開しても保存契約は別途必要であり、型付き検証や編集競合の問題も残る。ユーザー設定のライフサイクルに絞ったサービスを提供する。

### 独自のバリデータと手動構築 API を使う

既定値・検証・依存解決の仕組みを重複して提供することになる。標準 Options の登録と `IValidateOptions<T>` を使い、独自機構を永続化と編集の契約に絞る。

### 呼び出し側で JSON を直接読み書きする

小規模な用途では簡単だが、復旧、移行、競合検出、保存失敗時の動作が分散する。JSON の読み書きは標準機能を使い、管理規則をサービスに集約する。

### 変更差分だけを保存する

既定値の変更を反映しやすい一方、未指定・解除・削除・復元の区別が複雑になる。小規模なユーザー設定には全体保存を採用する。

### 編集ごとに自動保存し、保存前に実行中の設定を変更する

即時反映は容易だが、キャンセルと保存失敗時に巻き戻しが必要になる。編集コピーとプレビューを分離し、明示保存の成功後に設定を確定する。

## 結果と影響

- ファイルの読み込みは AddJsonFile と同じ構成ロード段階で完了し、利用者の初期化呼び出しは不要になる。
- 非同期ストアは構成登録前の await が必要であり、標準の同期 API 内でブロックしない。
- 利用者は共通ソースとモジュールの有効化を宣言するだけで、設定型の登録は各モジュールが担当する。
- 読み込みから保存・復旧までを同じ型付き API で扱える。
- 設定画面と Input 処理は JSON や保存媒体に依存しない。
- 標準 Options の DI・宣言的登録・バリデータと JSON 機能を活用できる。移行とストアは独自実装する。
- Options モデルは編集可能なため、公開時の深いコピーにコストがある。Input は Revision が変わった時だけ取得する。
- 全体保存により既存のユーザー値が維持される。既定値変更だけでは保存済みの値を更新しない。
- 保存成功と Input への反映には次のフレーム境界までの時間差がある。
- 原子的保存の保証と障害処理はストア実装ごとに検証する必要がある。

## 検証方針

偽ストアと実ストアを使って以下を検証する。共通基盤は Input に依存しないサンプルモジュールを使い、Settings.Tests で確認する。Input 固有の変換・フレーム反映に関する項目は、別の Input 実装で検証する。

- 共通基盤とモジュールの登録順に依存せず、二重有効化で既定値・検証を重複登録しない。
- 未登録の共通基盤、異なるソースの二重登録、セクション ID / 型の衝突を拒否する。
- Input と別モジュールの同時保存が両方残り、別モジュールの Revision を進めない。
- 未登録モジュールのセクションや他モジュールの不正なセクションを保存時に保持する。
- 全体破損とセクション破損で個別保存・個別リセットの許可を区別する。
- 全体復元の成功で古い編集を無効化し、失敗時は保護・全 Revision を維持する。
- 初回起動、通常読み込み、旧形式の移行、未指定項目の補完、明示的な空配列・0・false の保持。
- 不正データ、未知項目、未対応版、読み込み障害での診断と元データの保護。
- 明示リセットによる復旧、復旧失敗後の保護状態維持。
- 不正な設定候補を保存・公開せず、キー識別子としきい値を検証する。
- 保存成功後の再起動で同じリマッピングとデッドゾーンを復元する。
- 保存失敗、キャンセル、競合で Current と Revision が変化しない。
- コミット後のキャンセルで保存済み内容と Current が一致する。
- 同時保存を直列化し、古い Revision の候補を拒否する。
- ファイル置き換え失敗と Browser のトランザクション失敗で旧データを維持する。
- 公開コピーの内部コレクションまで確定値と分離し、Input の管理スレッドで一括切り替えする。
- ConfigurationBuilder の Build 時と ConfigurationManager の追加時に同期ロードが完了する。
- 非同期ストアは構成登録前に await され、provider.Load と DI 解決で追加 I/O や同期ブロックを行わない。
- 構成ロードと型付き生成・検証の時点を分離し、追加の ValidateOnStart 呼び出しなしで未参照の設定も Host 起動中に検証する。
- factory と編集サービスの解決順・並行解決にかかわらず、共通の初期値・診断・復旧保護が一度だけ確定する。
- 標準 Options のキャッシュや公開コピーを変更しても内部確定値が変化しない。
- 初期化失敗時に Configure / 移行 / 検証を暗黙に再実行しない。
- 既定辞書 `{A,B}` から B を削除して `{A}` を保存し、再起動でも B が復活しない。空辞書と未指定も区別する。
- `IOptions<T>` の初回生成は取得済み保存値を含み、保存後もキャッシュの契約を維持する。
- Configure → 保存 JSON の適用 → PostConfigure → 検証の順序と、保存時に Configure を再適用しないこと。
- 読み込み・保存・リセットで同じ標準バリデータを使い、複数の Fail を集約し Skip を尊重する。
- ラムダ、DataAnnotations、Options 検証ソースジェネレーターによるネスト・コレクション検証。
- デッドゾーンのゼロ、境界、斜め入力、外側の飽和、NaN / Infinity の拒否。
- NaN / Infinity の候補が DeepClone を通って標準バリデータに届き、ValidationFailed として戻る。
- JSON 変換失敗でストアを呼ばず、Current と Revision を維持する。
- プレビューのキャンセルが最新の確定済み設定へ戻る。

## 別途決定する事項

- Input のアクション型、複合入力、コンテキスト間の入力伝播と衝突規則。
- アクションの評価・複合入力・伝播。既定のアクション割り当てはアプリが定義し、共通の初期 Bindings は空とする。
- 永続的な物理デバイス識別と個体別プロファイル。
- Browser ストアの具体的な公開 API と IndexedDB のスキーマ。
- 複数プロセス・タブ間の競合制御、外部編集の再読み込み、バックアップ方針。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [ADR-INPUT-0001: デバイス別の入力記録と参照・通知・ポーリング](../input/INPUT-0001-input-system.md)
- [.NET の構成](https://learn.microsoft.com/dotnet/core/extensions/configuration)
- [System.Text.Json の概要](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)
- [.NET の Options パターン](https://learn.microsoft.com/dotnet/core/extensions/options)
- [Options 検証ソースジェネレーター](https://learn.microsoft.com/dotnet/core/extensions/options-validation-generator)
