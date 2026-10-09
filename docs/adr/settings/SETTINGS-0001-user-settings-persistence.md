# ADR-SETTINGS-0001: ユーザー設定の読み込み・検証・保存の一元管理

- 状態: 提案
- 日付: 2026-10-09
- 関連カテゴリ: input、platform

## 背景

Lumyte では Input のリマッピングとデッドゾーンをユーザーが編集し、次回起動時にも復元できるようにする。読み込みだけでなく、検証、保存、既定値への復元、実行中の設定への反映を同じ機構で管理する必要がある。

`Microsoft.Extensions.Configuration` は複数の設定ソースを読み込むための基盤であり、プロバイダーの値を変更しても JSON ファイルなどへの永続化は保証されない。ユーザー設定の保存には別の契約が必要である。

対象環境は Windows、Linux、Browser とする。[ADR-INPUT-0001](../input/INPUT-0001-input-system.md) の InputSystem は正規化された入力の記録を管理し、管理スレッドから利用する。設定ファイルの I/O やアクション変換を InputSystem に追加せず、上位層で接続する。

## 決定

### .NET 標準機能の利用範囲

ユーザー設定は .NET の Options パターンを拡張して管理する。DI 登録には `OptionsBuilder<T>` を使用し、既定値を `Configure`、正規化を `PostConfigure`、型付き設定値の検証を `IValidateOptions<T>` で宣言する。独自のバリデータ基盤は作らない。読み込み、形式移行、既定値の補完、原子的保存、編集競合、確定済み設定の公開を永続化サービスが担当する。JSON の読み書きには `System.Text.Json` を使用する。

`Microsoft.Extensions.Configuration` は必要に応じて起動時の構成や環境変数に使用する。ユーザーが編集する同じ項目に環境変数などの上書きを重ねず、保存した値と実際に使う値の対応を維持する。

本 ADR は設定管理の提案であり、公開 API と保存アダプターは未実装である。アクションの評価・入力伝播の詳細は後続 ADR に分離する。

### 責務と依存関係

| 構成要素 | 責務 |
| --- | --- |
| `Lumyte.Settings` | 汎用サービス、結果型、JSON の読み書き、保存先の共通契約 |
| 標準 Options の登録 | `Configure` / `PostConfigure` による既定値・正規化、`IValidateOptions<T>` による検証 |
| 設定定義 `ISettingsDefinition<T>` | JSON 型情報、形式移行、補完 |
| 設定ストア `ISettingsStore` | 保存媒体への読み込みと全体置き換え |
| アプリケーション / Engine の構成側 | ストアと設定定義の選択、設定画面、Input への橋渡し |
| Input の上位処理 | スナップショットからのリマッピング表生成とデッドゾーン変換 |

汎用ライブラリは将来 `src/Core/Lumyte.Settings/` に配置する。`Lumyte.Settings` は `Lumyte.Input` や OS 固有 API に依存しない。Input 設定の型と検証は Input 側、アプリ独自のアクション定義はアプリ側に置く。Browser のストアは Platform 側で実装する。プロジェクトは実装時に追加する。

### データモデルと互換性

設定ファイルは `{ "schemaVersion": 1, "values": { ... } }` のエンベロープとし、現在の設定全体を保存する。設定モデルは標準 Options と互換の引数なしコンストラクターを持つ class とする。UI は独立した編集コピーを持つ。保存呼び出し時に候補を JSON に変換して再構築し、以後の UI 編集と分離する。内部の確定済みモデルは外部に公開せず、公開スナップショットは内部コレクションまで深く複製する。取得したコピーを変更しても確定済み設定や他の利用者には影響しない。Input 側は取得したコピーから不変の変換表を構築する。

永続化サービスは JSON の項目の存在を確認して、新しく追加された未指定項目だけを既定値で補完する。明示的な `false`、`0`、空配列を未指定と扱わない。明示的な `null` の可否は JSON の型情報と標準バリデータで検証する。既存の保存値はアプリの既定値変更後も維持し、リセットで新しい既定値に戻す。

旧バージョンは JSON 上で順に移行し、補完、型付きモデルへの変換、検証を行う。読み込み時の移行だけでは元ファイルを書き換えない。対応バージョンより新しいファイルや、未知の項目を含み再保存によって情報を失うファイルは互換性エラーとして扱う。

形式の `schemaVersion` と、実行中の編集競合を検出する `Revision` は別である。Revision はサービス内の単調増加値で、ファイルには保存しない。

### DI と宣言的な登録

`AddPersistedOptions<T>` は標準 `OptionsBuilder<T>` を返す拡張メソッドとする。標準の `Configure`、`PostConfigure`、`Validate`、`ValidateDataAnnotations` と独自の保存先・JSON 形式の指定を連結できる。次の例は未実装の API を含む利用例である。

```csharp
services.AddPersistedOptions<InputSettings>()
    .Configure(options =>
    {
        options.LeftStick.Inner = 0.15f;
        options.LeftStick.Outer = 1.0f;
    })
    .ValidateDataAnnotations()
    .UseJsonFile(settingsPath)
    .UseJsonDefinition<InputSettings, InputSettingsJsonDefinition>();

services.AddSingleton<IValidateOptions<InputSettings>, InputSettingsValidator>();
```

`UseStore<T, TStore>` で Browser などの保存ストアを DI に登録できる。一つの設定型につき既定の Options 名だけを初期対象とし、独自 API から名前付き Options を登録しようとした場合は構成エラーにする。複数プロファイルは当面設定モデル内で表す。

利用側は `IEditableOptions<InputSettings>` をコンストラクター注入する。編集は `BeginEdit`、確定は `SaveAsync`、復元は `ResetAsync` で行う。DI の解決中に非同期 I/O を行わず、起動時に `InitializeAsync` を await してから Input や設定画面を開始する。Generic Host を使う場合も、構成側がこの初期化順を保証する。

### 標準バリデータの採用

型付きモデルの検証は DI に登録された `IEnumerable<IValidateOptions<T>>` を使用する。読み込み・保存・リセットのすべてで同じバリデータを実行し、`ValidateOptionsResult.Fail` の失敗文字列を集約する。既定の Options 名 `Options.DefaultName` を渡し、`Skip` は対象外として尊重する。バリデータは同期処理で、副作用を持たず、保存媒体への I/O を行わない。

単純な条件は `OptionsBuilder<T>.Validate`、属性の範囲・必須条件は `ValidateDataAnnotations`、デッドゾーンの有限値・項目間の大小関係や割り当ての衝突は `IValidateOptions<T>` で検証する。`Range` 属性だけでは `inner < outer` を表せない。DataAnnotations の通常のバリデータに任意のオブジェクトグラフの再帰検証を期待しない。ネストやコレクションを属性で検証する場合は標準の Options 検証ソースジェネレーターと `[ValidateObjectMembers]` / `[ValidateEnumeratedItems]` を使用する。

`ValidateOnStart` は標準 Options の生成を起動時に検証する機能であり、独自ストアからの読み込みや保存候補の検証を代行しない。永続化サービスの `InitializeAsync` と各書き込み操作で明示的に検証する。Browser / AOT では JSON と Options 検証のソースジェネレーターを活用し、反射を必須にしない登録方法を提供する。

`ValidateOptionsResult` の診断は文字列であり、標準契約に構造化された項目パス・エラーコードはない。初期設計では文字列をそのまま表示し、解析して項目パスを復元しない。JSON 形式、バージョン、I/O のエラーは永続化側の結果と区別する。

### 設定の構築順序

起動・リセットの既定値は、新しい T に名前が一致する `IConfigureOptions<T>` / `IConfigureNamedOptions<T>` を登録順に適用して作る。読み込みではこの既定値を JSON 化し、移行後の保存 JSON に存在する項目を上書きする。配列は全体置換、オブジェクトは項目単位で補完し、未知項目と不正な型を拒否する。その後、名前が一致する `IPostConfigureOptions<T>` を適用し、標準バリデータで検証する。

保存候補は現在の編集内容が全体を持つため、`Configure` を再適用してユーザー値を上書きしない。独立した候補に `PostConfigure` を適用してから検証し、正規化後の値を保存・公開する。`PostConfigure` は繰り返し適用しても結果が変わらない処理に限定する。

標準 `IOptionsFactory<T>.Create` は検証まで一括で実行するため、保存 JSON の適用前に検証してしまう。永続化サービスは標準の登録済みインターフェースを用いて上記の順序を組み立てる。標準 `IOptions<T>` / `IOptionsMonitor<T>` に保存済み設定を別途公開せず、確定値の読み取り窓口は `IEditableOptions<T>` に統一する。通常の Options API は本設定型の既定構成を生成するもので、永続化された確定値の参照先としては使用しない。

### 公開 API 案

比較元は `main` の `f69072fdf6089c006ac7a1c8af4712dee1b43579`。同 revision に `Lumyte.Settings` は存在しない。宣言は主要メンバーの抜粋である。`SettingsService<T>` は内部実装とし、標準 DI に singleton として登録する。

```diff
+namespace Lumyte.Settings
+{
+    // Value は内部確定値と分離した深いコピー。Revision はサービス内の版。
+    public sealed record SettingsSnapshot<T>(long Revision, T Value) where T : class, new();
+    // Value は編集可能。BaseRevision と生成元サービスの識別を保持する。
+    public sealed class SettingsEdit<T> where T : class, new()
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
+    public sealed record SettingsSaveResult<T>(
+        SettingsSaveStatus Status, SettingsSnapshot<T> Snapshot,
+        ImmutableArray<string> Errors) where T : class, new();
+    public interface ISettingsDefinition<T> where T : class, new()
+    {
+        int SchemaVersion { get; }
+        JsonTypeInfo<T> JsonTypeInfo { get; }
+        // JSON 形式の移行のみ。型付き検証は IValidateOptions<T> に委譲する。
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
+    public interface IEditableOptions<T> where T : class, new()
+    {
+        // 同時初期化は InvalidOperationException。完了後は同じ結果を返し、再読込しない。
+        // キャンセルは再試行可能。不正な既定値は OptionsValidationException。
+        Task<SettingsLoadResult> InitializeAsync(CancellationToken cancellationToken = default);
+        // 初期化前は InvalidOperationException。任意スレッドから一貫して取得。
+        SettingsSnapshot<T> Current { get; }
+        // コピーなしの変更検出用。Current の Revision を最終的な基準とする。
+        long Revision { get; }
+        SettingsEdit<T> BeginEdit();
+        // 検証 → Revision 確認 → 保存 → 確定値置換。別サービスの edit は引数エラー。
+        Task<SettingsSaveResult<T>> SaveAsync(
+            SettingsEdit<T> edit, CancellationToken cancellationToken = default);
+        // 既定値を構築し、同じ検証・保存手順で確定。読込障害後の明示復旧にも使う。
+        Task<SettingsSaveResult<T>> ResetAsync(
+            long expectedRevision, CancellationToken cancellationToken = default);
+    }
+    public static class PersistedOptionsExtensions
+    {
+        // 既定名のみ。通常の Configure / PostConfigure / Validate を連結できる。
+        public static OptionsBuilder<T> AddPersistedOptions<T>(
+            this IServiceCollection services) where T : class, new();
+        // 次の登録は設定型ごとに一つ。重複したストア・定義は構成エラー。
+        public static OptionsBuilder<T> UseJsonFile<T>(
+            this OptionsBuilder<T> builder, string path) where T : class, new();
+        public static OptionsBuilder<T> UseStore<T, TStore>(this OptionsBuilder<T> builder)
+            where T : class, new() where TStore : class, ISettingsStore;
+        public static OptionsBuilder<T> UseJsonDefinition<T, TDefinition>(
+            this OptionsBuilder<T> builder)
+            where T : class, new() where TDefinition : class, ISettingsDefinition<T>;
+    }
+}
```

### 読み込み・編集・保存の契約

起動時に DI から解決した `IEditableOptions<T>.InitializeAsync` を一度呼び、結果の診断を UI やログへ渡す。ファイル不在なら既定値で開始し、暗黙にファイルを作成しない。不正データ、未対応版、ストア障害では既定値で開始するが、通常の `SaveAsync` は `RecoveryRequired` を返し、元データを上書きしない。ユーザーが明示的に復元を選んだ場合の `ResetAsync` でのみ上書きし、成功後に通常保存を許可する。

UI は編集開始時の `Current.Revision` を保持する。保存は全設定を一括で検証し、失敗なら標準バリデータの失敗文字列を返す。保存操作の直列化後に Revision を確認し、古い編集コピーなら `Conflict` を返す。成功時だけ保存内容と同じスナップショットを公開し、Revision を増やす。保存失敗では Current と Revision を維持し、UI の編集コピーから再試行できる。

内部の確定値全体を一度に置き換え、取得済みのコピーを変更しない。通知コールバックを保存処理に組み込まず、利用側がフレーム開始時などに Revision の変化を検出する。

引数違反と設定定義のプログラミングエラーは例外とする。キャンセルはコミット前なら `OperationCanceledException` とし、設定を変更しない。ストアはコミット開始直前に最後のキャンセル判定を行い、以降は結果を確定させる。コミット成功後は Current の公開まで完了し、キャンセルを保存失敗として報告しない。

一つのストアを一つのサービスが所有する構成とする。Revision は同じサービス内の編集競合を防ぐもので、複数プロセス、複数 Browser タブ、外部エディターとの競合検出や自動リロードは初期対象に含めない。注入ストアのリソースは構成側が所有し、保存完了を待ってから解放する。

### 保存媒体と障害時の扱い

Windows / Linux は構成側が選んだユーザー単位の設定ディレクトリを使用する。`JsonFileSettingsStore` は保存先と同じディレクトリの一時ファイルに書き込み、フラッシュ・クローズ後に置き換える。対応するファイルシステムで原子的な置き換えを使用し、それを保証できない保存先は失敗として報告する。失敗時に保存先を先に削除する実装は行わない。電源断に対する耐久性は OS とファイルシステムに依存し、原子的な可視性と区別する。

Browser はファイルパスを前提にせず、Platform が IndexedDB のトランザクションで置き換えるストアを注入する。トランザクションの完了を保存成功とし、容量不足、利用不可、トランザクション失敗を `StorageFailure` として報告する。保存できない状態を成功扱いしてメモリだけで確定しない。サイトデータの消去やブラウザーによる退避解除に対する永続性は保証しない。

### Input 設定との接続

アクションは表示名と分離した安定した文字列 ID で識別し、コンテキスト単位で複数の物理入力を割り当てられるようにする。各アクションの割り当て配列は全体置換し、未指定は既定値、空配列は明示的な解除とする。キーやコントローラーの識別子は安定した文字列表現で保存し、enum の数値や実行中だけ有効な `InputDeviceId` は永続化しない。

デバイス種別・プロファイルを設定の単位とする。現行 InputDeviceInfo は再接続後も安定する物理 ID を持たないため、個体別設定は後続のデバイス識別設計まで提供しない。入力の重複はコンテキストとアクション定義の規則で検証する。

デッドゾーンは有限値で `0 <= inner < outer <= 1` を満たすものとする。スティックは長さ r を使う放射状の変換を基本とし、出力の長さを `clamp((r - inner) / (outer - inner), 0, 1)` とする。r が 0 の場合はゼロを返し、方向は維持する。トリガーは正規化済みの 0〜1 の値に同じしきい値変換を行う。具体的なしきい値の既定値は Input 設定定義で別途定める。

InputSystem の入力記録・現在状態は元の正規化値を保持する。デッドゾーンとリマッピングは記録の読み取り後に上位層で処理する。構成側は管理スレッドのフレーム開始時に Revision を確認し、変更時だけ Current の深いコピーを取得して、変更された Revision に対して変換表をまとめて切り替える。サービスの保存成功は確定済み設定の公開を意味し、Input への適用は次のフレーム境界で完了する。

Input の `IValidateOptions<T>` は変換表を構築できることまで保存前に検証する。即時プレビューは UI の編集コピーから一時的な変換表を作り、保存せず試す。キャンセル時は最新の確定済みスナップショットへ戻す。保存中に編集が続いた場合、保存候補以降の編集を未保存として維持する。

## 検討した代替案

### Microsoft.Extensions.Configuration を直接使用する

読み込みと複数ソースの統合には適しているが、編集・保存の保証がなく、アクション配列の統合も全体置換の規則と一致しない。ユーザー設定の中核には使用しない。

### IConfiguration 全体を独自ラッパーで包む

標準機能の読み込み API を再公開しても保存契約は別途必要であり、型付き検証や編集競合の問題も残る。ユーザー設定のライフサイクルに絞ったサービスを提供する。

### 独自のバリデータと手動構築 API を使う

既定値・検証・依存解決の仕組みを重複して提供することになる。標準 Options の登録と IValidateOptions<T> を使い、独自機構を永続化と編集の契約に絞る。

### 呼び出し側で JSON を直接読み書きする

小規模な用途では簡単だが、復旧、移行、競合検出、保存失敗時の動作が分散する。JSON の読み書きは標準機能を使い、管理規則をサービスに集約する。

### 変更差分だけを保存する

既定値の変更を反映しやすい一方、未指定・解除・削除・復元の区別が複雑になる。小規模なユーザー設定には全体保存を採用する。

### 編集ごとに自動保存し、保存前に実行中の設定を変更する

即時反映は容易だが、キャンセルと保存失敗時に巻き戻しが必要になる。編集コピーとプレビューを分離し、明示保存の成功後に設定を確定する。

## 結果と影響

- 読み込みから保存・復旧までを同じ型付き API で扱える。
- 設定画面と Input 処理は JSON や保存媒体に依存しない。
- 標準 Options の DI・宣言的登録・バリデータと JSON 機能を活用できる。移行とストアは独自実装する。
- Options モデルは編集可能なため、公開時の深いコピーにコストがある。Input は Revision が変わった時だけ取得する。
- 全体保存により既存のユーザー値が維持される。既定値変更だけでは保存済みの値を更新しない。
- 保存成功と Input への反映には次のフレーム境界までの時間差がある。
- 原子的保存の保証と障害処理はストア実装ごとに検証する必要がある。

## 検証方針

実装時に偽ストアと実ストアを使い、以下を検証する。本 PR は設計文書のみを追加する。

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
- DI での解決、非同期初期化の順序、初期化前アクセス、複数の初期化呼び出し。
- Configure → 保存 JSON の適用 → PostConfigure → 検証の順序と、保存時に Configure を再適用しないこと。
- 読み込み・保存・リセットで同じ標準バリデータを使い、複数の Fail を集約し Skip を尊重する。
- ラムダ、DataAnnotations、Options 検証ソースジェネレーターによるネスト・コレクション検証。
- デッドゾーンのゼロ、境界、斜め入力、外側の飽和、NaN / Infinity の拒否。
- プレビューのキャンセルが最新の確定済み設定へ戻る。

## 別途決定する事項

- Input のアクション型、複合入力、コンテキスト間の入力伝播と衝突規則。
- Input 設定モデルの具体的な公開 API、既定の割り当て・デッドゾーン値。
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
