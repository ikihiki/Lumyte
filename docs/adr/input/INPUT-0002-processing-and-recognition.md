# ADR-INPUT-0002: デバイスデータ補正と仮想デバイス生成

- 状態: 提案（実装を含む）
- 日付: 2026-10-09

## 背景

[Input 基盤](INPUT-0001-input-system.md)は DI で受け取った InputSource からデバイスを登録し、デバイス別の履歴・通知・ポーリングを提供する。デッドゾーン、中心ずれ、筆圧、平滑化、タッチ操作の変換を差し込む位置と、[設定保存基盤](../settings/SETTINGS-0001-user-settings-persistence.md)との連携を統一する。

## 決定

### 加工の区分

| 層 | 入力と出力 | 担当 |
| --- | --- | --- |
| デバイス補正 | 同じデバイスの InputData → 補正済み InputData | Processing |
| 仮想デバイス生成 | 物理デバイスのバッチ → 別の Logical デバイス | Processing |
| アクション値補正 | マッピング結果 → 補正済み ActionState | Actions |
| 操作認識 | アクション遷移 → 意味のある操作 | Actions |

本 ADR は前の二つを扱う。[INPUT-0003](INPUT-0003-actions-and-contexts.md)は後の二つ、入力バッファ、リバインド、コンテキストを扱う。InputSystem の記録・保持期間の責務を変更しない。

### デバイス補正

`Lumyte.Input.Processing` は共通 Input と .NET にだけ依存する。`CorrectingInputSource` は DI で借用した Source に Registry のデコレータを渡し、登録された Device を `CorrectedInputDevice` で包む。プロセッサはデバイスごとに独立して生成し、指定順に実行する。

`AnalogCorrection` はスティック中心補正、半径方向のデッドゾーン、タッチ・ペンの筆圧カーブを扱う。半径がデッドゾーン以下ならゼロ、それ以上は残りの区間を 0〜1 に再配置する。筆圧が null ならそのまま保持する。`ExponentialStickSmoothing` は左右のスティックを別々に単調時間差で平滑化し、フォーカス喪失で履歴をリセットする。

InputSystem が記録・通知・ポーリングに使うのは補正後のデータである。元入力を別途保存する場合は、元デバイスの取得側に診断用の分岐を設ける。

補正処理が失敗した場合は、取得済みのバッチと取得時刻、完了した加工段を保持し、次の DrainEvents で失敗した段から再試行する。復旧時は追加の入力も一度取得し、保持した出力と合わせて公開する。プロセッサは Process / Reset の失敗時に自身の状態を変更しない。再試行中に予約された設定交換は、そのバッチが完了した後の取得境界で適用する。

通常の取得では加工済みの不変バッチを直接返し、復旧などで複数バッチをまとめる場合だけ出力を合成する。追跡や補正で内容が変わらない場合は入力バッチを再利用する。再試行の進捗は内部で再利用できるが、公開済みバッチを作業領域として書き換えない。空入力でも平滑化などの時間による更新を実行する。

`SetProcessorFactory` は既存デバイスの新しいパイプラインを準備してから交換を予約する。次の DrainEvents でフォーカス喪失による中立化を発生させ、元のフォーカス状態へ戻す。進行中のタッチ・ペン ID の続きは終了まで除外し、新しい接触だけを受け付ける。

### 仮想デバイス生成

`VirtualizingInputSource` は元 Source の Registry を包み、対象デバイスと Logical Controller を登録する。Update で元デバイスのバッチを一度だけ取得し、物理側と仮想側のキューを同じ更新内に準備する。InputSystem の履歴を仮想 Device の DrainEvents から読み直すことはしない。

`TouchControllerGenerator` は次を出力する。

| タッチ操作 | コントローラー入力 |
| --- | --- |
| 一接触の開始点からの移動 | Left Stick |
| 正常終了した十分な距離のスワイプ | South の押下・解放 |
| 二接触の初期距離からのピンチ | Right Stick X |
| 二接触の初期角度からの回転 | Right Stick Y |

接触の追加・終了ではピンチと回転の基準を取り直す。Canceled はスワイプにならず、フォーカス喪失・切断では操作を中断する。新しい接触が始まるまで終了済みの接触を復元しない。原点 ID は履歴上の物理デバイス ID、生成先 ID は独立した InputSystem の ID とする。

Source の構成は内側から外側へ取得が進む木構造であり、実装は物理デバイスごとの直接生成を提供する。履歴を再入力する循環経路や動的な派生グラフは公開しない。複数段の一般的な派生グラフを追加する場合は、トポロジカル順序と循環拒否を別途実装する。

### 所有権と終了処理

補正・仮想 Source は内側 Source を借用し、Dispose で内側を破棄しない。登録成功後の Device は InputSystem が所有し、物理デバイスを一度だけ破棄する。登録失敗時は元 Device を呼び出し元の所有として残す。仮想側の登録失敗では物理ラッパーの所有権を解除し、部分登録を取り消す。

切断では最後のバッチを物理・仮想へ取り込み、両方の登録解除を予約する。Shutdown でも元 Source の終了後に残りのバッチを取り込む。設定・加工・取得は InputSystem の管理スレッドで実行する。

元 Source や個々のデバイスの取得・生成が失敗しても、残りのデバイスの処理を続ける。切断時は最終取得の成否によらず物理・仮想の両方を登録解除し、処理後に例外を集約して報告する。

### DI 構成と InputSystem との相互作用

構成側が Microsoft.Extensions.DependencyInjection を使用する。Processing 自体に DI コンテナーへの依存を追加しない。PlatformInputSource は各プラットフォームのバックエンドの例示名である。

```csharp
services.AddSingleton<PlatformInputSource>();
services.AddSingleton<InputTimeSource>();
services.AddSingleton(provider => new CorrectingInputSource(
    provider.GetRequiredService<PlatformInputSource>(),
    descriptor => descriptor.Kind == InputDeviceKind.Controller
        ? new IDeviceDataProcessor[]
        {
            new AnalogCorrection(Vector2.Zero, deadZone: 0.15f),
            new ExponentialStickSmoothing(0.05f),
        }
        : [],
    provider.GetRequiredService<InputTimeSource>().GetElapsedTime));
services.AddSingleton(provider => new VirtualizingInputSource(
    provider.GetRequiredService<CorrectingInputSource>(),
    descriptor => descriptor.Kind == InputDeviceKind.Touch
        ? new TouchControllerGenerator(radius: 100, swipeDistance: 80)
        : null,
    provider.GetRequiredService<InputTimeSource>().GetElapsedTime));
services.AddSingleton<IInputSource>(provider =>
    provider.GetRequiredService<VirtualizingInputSource>());
services.AddSingleton(provider =>
{
    var input = new InputSystem(provider.GetServices<IInputSource>());
    provider.GetRequiredService<InputTimeSource>().Attach(input);
    return input;
});
```

InputSystem は外側の IInputSource を受け取る。内側を同時に IInputSource として登録すると同じ機器を二重取得するため、具象型だけで登録する。InputTimeSource は InputSystem 構築後に接続し、更新・終了処理では DI を再解決せずに ElapsedTime を参照する。構築前はゼロを返す。

一回の更新は Source.Update → 補正済みバッチ取得 → 仮想入力生成 → InputSystem の物理・仮想 Device 取り込み → 記録・状態更新・通知となる。履歴の保持期間は既存の SetRetentionPolicy / ClearRecords 等で利用者が設定する。

### 設定保存

任意の連携ライブラリ `Lumyte.Input.Settings` が Processing・Actions・Settings を参照する。実行コアは Settings に依存しない。`AddInputSettings` は `input-processing` と `input-actions` を登録し、ソース生成 JSON メタデータと構成可能性のバリデータを設定する。

InputProcessingSettings は名前付き DeviceProfiles、TouchRadius、SwipeDistance を持つ。プロファイルには CenterX / CenterY、DeadZone、PressureExponent、SmoothingSeconds を保存する。実行 ID、接触、平滑化履歴は保存しない。既定の選択キーは Controller / Touch / Pen 等のデバイス種別名であり、独自の `selectProfile` で記述子を永続的な利用者プロファイル名へ対応付けられる。

```csharp
var source = new PersistedJsonFileSource(absolutePath);
configuration.AddPersistedJsonFile(source);
services.AddSettings(source);
services.AddInputSettings();
// Host を使わない場合も、入力開始前に呼ぶ。
provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();

var editable = provider.GetRequiredService<
    IEditableOptions<InputProcessingSettings>>();
var edit = editable.BeginEdit();
edit.Value.DeviceProfiles["Controller"] = new DeviceCorrectionSettings
{
    DeadZone = 0.2f,
    PressureExponent = 1.5f,
};
var saved = await editable.SaveAsync(edit, cancellationToken);
// Saved のときだけ Current と Revision が変わる。
```

管理スレッドで InputSettingsCoordinator.ApplyCommittedSettings → input.Update → actions.Advance の順に実行する。Coordinator は Revision の変化時だけ Current のコピーを変換する。保存の継続スレッドから Device を操作しない。検証失敗・競合・保存失敗では確定済み設定を維持する。セクションごとの Revision を全体トランザクションとは見なさない。

[実行サンプル](../../../samples/Lumyte.Input.Advanced.Sample/Program.cs)は DI、実際の設定ファイル、リバインド、記録通知からアクションへの受け渡しを示す。プラットフォームバックエンドの代わりに決定的なデモ Source を使う。

### 公開 API 一覧

主要な追加 API を差分形式で示す。既存 InputSystem の API は ElapsedTime を除いて維持する。

```diff
--- /dev/null
+++ b/INPUT-0002-processing-and-recognition-public-api.txt
@@ -0,0 +1,29 @@
+InputSystem.ElapsedTime : TimeSpan
+InputTimeSource.GetElapsedTime() : TimeSpan
+InputTimeSource.Attach(InputSystem system)
+IDeviceDataProcessor.Process(IReadOnlyList<InputData> data, TimeSpan now)
+IDeviceDataProcessor.Reset()
+AnalogCorrection(Vector2 center, float deadZone = 0.15f,
+    float pressureExponent = 1)
+ExponentialStickSmoothing(float seconds)
+CorrectedInputDevice(IInputDevice inner,
+    IEnumerable<IDeviceDataProcessor> processors, Func<TimeSpan> getTime)
+CorrectedInputDevice.SetProcessors(IEnumerable<IDeviceDataProcessor> processors)
+CorrectingInputSource(IInputSource inner,
+    Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory,
+    Func<TimeSpan> getTime)
+CorrectingInputSource.SetProcessorFactory(
+    Func<InputDeviceDescriptor, IEnumerable<IDeviceDataProcessor>> factory)
+IVirtualDeviceGenerator.Generate(InputDeviceId origin,
+    IReadOnlyList<InputData> data, TimeSpan now)
+IVirtualDeviceGenerator.Reset(InputDeviceId origin, TimeSpan now)
+TouchControllerGenerator(float radius = 100, float swipeDistance = 80)
+VirtualizingInputSource(IInputSource inner,
+    Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory,
+    Func<TimeSpan> getTime)
+VirtualizingInputSource.SetGeneratorFactory(
+    Func<InputDeviceDescriptor, IVirtualDeviceGenerator?> factory)
+IServiceCollection.AddInputSettings()
+InputSettingsConverter.BuildCorrections(InputProcessingSettings settings,
+    Func<InputDeviceDescriptor, string>? selectProfile = null)
+InputSettingsCoordinator.ApplyCommittedSettings()
```

## 検討した代替案

- InputSystem に全加工を組み込む: デバイス履歴と利用者ごとの設定の責務が混ざる。
- 仮想 Device から履歴を読む: 同じ更新では未記録のため、一更新遅れる。
- 全処理をマッピング後に置く: 接触 ID や筆圧などの元情報が失われる。

## 結果と検証

既存の履歴・通知・ポーリングを保ったまま加工を差し込める。補正前の診断分岐、実 OS バックエンド、一般的な派生グラフの構成 UI は本実装の対象外とする。

テストでは中心補正・デッドゾーン・筆圧 null、一更新一取得、スワイプの終了とキャンセル、ピンチ・回転、設定変更時の接触中断、所有権と設定保存・復元を確認する。
