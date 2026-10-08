# ADR-INPUT-0001: デバイス別の入力記録と参照・通知・ポーリング

- 状態: 提案
- 日付: 2026-10-08

## 背景

Lumyte は Windows、Linux、Browser を対象とする C# のゲームエンジンである。OS 固有の入力 API をゲーム側で直接扱うと、入力状態と履歴の管理が利用コードに分散する。

入力データはデバイスごとに記録し、利用者が記録配列を直接参照する方法、イベントで通知を受ける方法、ポーリングで取得する方法を選べるようにしたい。利用者によって処理周期が異なるため、フレーム更新のたびに履歴を一律に消去すると、低頻度の利用者が短い押下などを取りこぼす。古いデータの削除時点も利用者が設定できる必要がある。

本 ADR は最初の Input 実装に向けた設計案である。環境固有の入力ライブラリ選定は後続 ADR に分離する。

## 決定

### 目的と対象範囲

InputSystem はデバイスごとの入力記録配列と最新状態を管理する。配列参照、イベント通知、ポーリングは同じ記録を利用し、読み取りによって記録を消費しない。入力の取得周期と履歴の保持期間は独立させ、毎フレームの削除を必須にしない。

初期対象は物理キー、マウスボタン、位置、移動量、ホイール、コントローラー（ゲームパッド）のボタン・スティック・トリガー、マルチタッチ、ペンの位置・接触・筆圧、フォーカス、デバイス接続・切断とする。文字入力・IME、アクションマッピング、保存形式は後続 ADR とする。記録の基盤はデバイスの追加を想定するが、これらの入力種別が実装済みであるとは扱わない。

InputSystem はコンストラクター DI で受け取るすべての IInputSource と、それらが登録する IInputDevice を一元管理する。ウィンドウ入力、コントローラー入力など複数のソースを同時に登録できる。ソースやデバイスは自身の入力対象を把握し、フォーカス変更はその対象に属するデバイスだけに通知する。識別できる物理デバイスは分離して記録する。物理デバイスを識別できない API、特に Browser のキーボード・マウスは種類ごとの論理デバイスとして記録し、その制約をデバイス情報に明示する。

### 責務と依存関係

[リポジトリのフォルダ構成](../0002-repository-layout.md) に従い、共通ライブラリを `src/Platform/Lumyte.Input/` に配置する。プロジェクト名、NuGet パッケージ名、名前空間は `Lumyte.Input` とする。

| 構成要素 | 責務 | 依存先 |
| --- | --- | --- |
| `Lumyte.Input` | 全入力ソースと登録デバイスの管理、記録配列、状態更新、参照・通知・ポーリング、保持方針 | .NET 標準ライブラリのみ |
| `Lumyte.Platform.Windows` / `Linux` / `Browser` | 接続監視と登録を行う `IInputSource`、入力取得・正規化を行う `IInputDevice` の実装 | `Lumyte.Input` |
| `Lumyte.Engine` | DI スコープと InputSystem のライフサイクル管理、利用する全 Source の DI 登録、取得・利用・削除の呼び出し順 | 共通 Input と必要な Platform 実装 |
| ゲーム・ツール | デバイスの選択、記録参照やイベント購読、ポーリング、保持方針の設定 | `Lumyte.Input` の公開 API |

共通 Input は Engine、Platform 実装、Graphics に依存しない。OS ハンドルやウィンドウ型を共通 API に持ち込まない。この PR は文書だけを追加し、空のプロジェクトを作成しない。

### 入力ソースの依存性注入

InputSystem は `IEnumerable<IInputSource>` をコンストラクターで受け取る。構成側が利用する Platform の Source を DI に登録し、InputSystem は渡されたすべての Source を一元管理する。共通ライブラリは特定の DI コンテナーに依存せず、手動のコンストラクター注入も可能とする。

Source 列挙は構築時に一度だけ行い、順序を維持した読み取り専用一覧へ固定する。null の列挙・要素は拒否し、同じインスタンスの重複は拒否する。空の一覧はヘッドレス用途に許容する。Source の追加・削除 API は公開しない。構成変更は新しい DI スコープと InputSystem の生成で行い、デバイスの接続・切断は既存 Source の Registry 操作で扱う。

InputSystem と Source は同じ寿命の DI スコープに置き、一つの Source インスタンスを複数 InputSystem に共有しない。ソース一覧の管理とソースオブジェクトの所有権を区別し、注入された Source 自体の破棄は DI コンテナーまたは構成側が行う。

### データモデル

- `InputDeviceId` は一つの InputSystem 内で一意な不透明 ID とする。切断後に同じ ID を再利用せず、再接続は新しい ID とする。永続 ID や異なる InputSystem 間の識別子としては使わない。
- `InputDeviceInfo` は InputSystem が割り当てる ID、種類、表示名、物理デバイスか論理デバイスかを表す識別粒度を持つ。
- `InputRecord` はデバイス ID、InputSystem 全体で単調増加する Sequence、取り込み時刻、正規化された入力データを持つ不変値とする。Sequence は 1 から開始し、削除後もリセットしない。
- 記録時刻は注入された `TimeProvider` の単調時計による取り込み時の経過時間とする。OS の発生時刻とは区別する。同じバッチ内で時刻が等しくても Sequence で順序を決められる。
- 各デバイスは Sequence 順の保持記録配列と最新状態を持つ。押下状態などは履歴とは別に保持し、履歴削除によって現在押されているキーを解放しない。
- 接続・切断とフォーカスの変化も記録する。フォーカス変化は同じ入力対象の各接続デバイスから取得し、合成解放とともに通常の記録経路へ流す。

### 公開 API 一覧

以下は `Lumyte.Input` 名前空間に追加する主要 API を diff 形式で示した設計案である。すべて新規追加のため `+` 行とする。宣言の本体と内部メンバーを省略した API 一覧であり、コンパイル用の実装ではない。

```diff
+using System;
+using System.Collections.Generic;
+using System.Numerics;
+
+namespace Lumyte.Input;
+
+// 0 は無効。InputSystem が割り当て、全ソースを通じて再利用しない。
+public readonly record struct InputDeviceId(ulong Value);
+public enum InputDeviceKind { Keyboard, Mouse, Controller, Touch, Pen }
+public enum InputDeviceIdentityKind { Physical, Logical }
+public sealed record InputDeviceInfo(
+    InputDeviceId Id, InputDeviceKind Kind, string Name,
+    InputDeviceIdentityKind IdentityKind);
+
+// Key の完全なメンバー一覧はバックエンド設計時に確定する。
+public enum Key { Unknown, /* 文字位置・数字・矢印・左右別修飾キー等 */ }
+public enum MouseButton { Left, Right, Middle, X1, X2 }
+// フェイスボタンはメーカーの文字表記ではなく位置で表す。
+public enum ControllerButton
+{
+    South, East, West, North,
+    DPadUp, DPadDown, DPadLeft, DPadRight,
+    LeftShoulder, RightShoulder, LeftStick, RightStick, Start, Select,
+}
+public enum ControllerStick { Left, Right }
+public enum ControllerTrigger { Left, Right }
+
+// 共通ライブラリが定義する派生型に限定する。
+public abstract record InputData
+{
+    private protected InputData();
+}
+public sealed record KeyData(Key Key, bool IsDown, bool IsRepeat) : InputData;
+public sealed record MouseButtonData(MouseButton Button, bool IsDown) : InputData;
+public sealed record MouseMoveData(Vector2 Position) : InputData;
+public sealed record MouseWheelData(Vector2 Delta) : InputData;
+public sealed record ControllerButtonData(
+    ControllerButton Button, bool IsDown) : InputData;
+// 各軸 -1〜1。中心 0、右が X 正、上が Y 正。
+public sealed record ControllerStickData(
+    ControllerStick Stick, Vector2 Value) : InputData;
+// 0〜1。未操作 0、最大操作 1。
+public sealed record ControllerTriggerData(
+    ControllerTrigger Trigger, float Value) : InputData;
+public sealed record DeviceConnectedData(InputDeviceInfo Info) : InputData;
+public sealed record DeviceDisconnectedData : InputData;
+// ContactId は一つの Device 内で一意。終了・キャンセル後も再利用しない。
+public readonly record struct TouchContactId(ulong Value);
+public enum TouchPhase { Began, Moved, Ended, Canceled }
+// Pressure は 0〜1、取得不能なら null。Position は共通の論理座標。
+public sealed record TouchData(
+    TouchContactId ContactId, TouchPhase Phase,
+    Vector2 Position, float? Pressure) : InputData;
+
+// PointerId は一つの Device 内のペン識別子。離脱後の再入場は新しい ID。
+public readonly record struct PenPointerId(ulong Value);
+public enum PenPhase { Entered, Moved, Down, Up, Left, Canceled }
+[Flags]
+public enum PenButtons { None = 0, Barrel = 1, SecondaryBarrel = 2 }
+// ホバー・筆先接触・消しゴムを区別し、未対応の筆圧を捏造しない。
+public sealed record PenData(
+    PenPointerId PointerId, PenPhase Phase, Vector2 Position,
+    float? Pressure, bool IsInContact, PenButtons Buttons, bool IsEraser) : InputData;
+public sealed record FocusData(bool IsFocused) : InputData;
+
+// 配列・通知・ポーリングで共通の不変記録。
+public readonly record struct InputRecord(
+    InputDeviceId DeviceId, ulong Sequence, TimeSpan RecordedAt, InputData Data);
+// デバイス登録前の情報。ID は InputSystem が付与する。
+public sealed record InputDeviceDescriptor(
+    InputDeviceKind Kind, string Name, InputDeviceIdentityKind IdentityKind);
+
+// Source は接続・切断を監視し、渡された Registry に登録・削除する。
+public interface IInputSource : IDisposable
+{
+    void Initialize(IInputDeviceRegistry registry);
+    void Update();
+    // InputSystem との接続・監視を解除する。Source 自体の破棄とは別。
+    void Shutdown();
+}
+
+// InputSystem が Source ごとに渡す登録窓口。他ソースのデバイスは削除不可。
+public interface IInputDeviceRegistry
+{
+    InputDeviceId RegisterDevice(IInputDevice device);
+    void UnregisterDevice(InputDeviceId device);
+}
+
+// 入力取得は Device の責務。接続・切断データは InputSystem が生成する。
+public interface IInputDevice : IDisposable
+{
+    InputDeviceDescriptor Descriptor { get; }
+    // 開始時点の未処理入力を受信順に返す。取得中の到着分は次回へ。
+    IReadOnlyList<InputData> DrainEvents();
+}
+
+// 両方 null が Manual。上限 0 は許容し、負値は拒否する。
+public sealed record InputRetentionPolicy(
+    int? MaxRecords = null, TimeSpan? MaxAge = null);
+
+public sealed class InputSystem : IDisposable
+{
+    // 全 Source を DI で受け取り、一度だけ列挙して順序を固定する。
+    // Source は借用し、時計の既定は TimeProvider.System。
+    public InputSystem(
+        IEnumerable<IInputSource> sources, TimeProvider? timeProvider = null);
+    public IReadOnlyList<IInputSource> Sources { get; }
+    // アクティブな IInputDevice のみ。削除後の履歴は別に保持する。
+    public IReadOnlyDictionary<InputDeviceId, IInputDevice> Devices { get; }
+    // Source を切り離し、所有する Device を解放する。Source 自体は破棄しない。
+    // 複数回の呼び出しは許容する。
+    public void Dispose();
+    // 全 Source を注入順に更新後、全 Device の入力を取り込む。
+    // 状態更新・通知・自動削除。表示フレームへの依存なし。
+    public void Update();
+    // 切断済みを含むメタデータの一覧。
+    public IReadOnlyList<InputDeviceInfo> DeviceInfos { get; }
+    // 保持記録の不変配列を参照。Span で列挙、ToArray で所有配列化できる。
+    public ReadOnlyMemory<InputRecord> GetRecords(InputDeviceId device);
+    // 現在状態の不変スナップショット。履歴の有無に依存しない。
+    public InputDeviceState GetState(InputDeviceId device);
+    // 指定 Sequence より後の記録。利用者ごとにカーソルを管理する。
+    public InputReadResult ReadRecords(InputDeviceId device, ulong afterSequence);
+    // 新規記録ごとに一度、Sequence 順で同期通知。DeviceId で選別する。
+    public event Action<InputRecord>? Recorded;
+    // 既定は Manual。設定変更は次の自動削除または明示削除から適用。
+    public InputRetentionPolicy GetRetentionPolicy(InputDeviceId device);
+    public void SetRetentionPolicy(InputDeviceId device, InputRetentionPolicy policy);
+    // 全デバイスの保持方針を評価し、削除件数を返す。
+    public int Prune();
+    // Sequence 以下の履歴を削除し、削除件数を返す。
+    public int RemoveRecordsThrough(InputDeviceId device, ulong sequence);
+    // 全履歴を削除。現在状態・デバイス情報・採番は維持する。
+    public int ClearRecords(InputDeviceId device);
+}
+
+// InputSystem が生成する不変スナップショット。公開コンストラクターなし。
+public sealed class InputDeviceState
+{
+    public bool IsConnected { get; }
+    public bool IsFocused { get; }
+    // 初期状態は解放。対象種別に合わない照会は InvalidOperationException。
+    public bool IsDown(Key key);
+    public bool IsDown(MouseButton button);
+    public bool IsDown(ControllerButton button);
+    // Mouse のみ。移動・ホイールの期間集計は履歴から行う。
+    public Vector2 MousePosition { get; }
+    // Touch のみ。現在接触中の指を保持し、終了・キャンセル後は一覧から削除。
+    public IReadOnlyDictionary<TouchContactId, TouchData> TouchContacts { get; }
+    // Pen のみ。ホバー中を含む在圏ペン。Left / Canceled で一覧から削除。
+    public IReadOnlyDictionary<PenPointerId, PenData> PenPointers { get; }
+    // Controller のみ。初期値 0、デッドゾーン適用前の正規化値。
+    public Vector2 GetStick(ControllerStick stick);
+    public float GetTrigger(ControllerTrigger trigger);
+}
+
+// InputSystem が生成する取得結果。公開コンストラクターなし。
+public sealed class InputReadResult
+{
+    public ReadOnlyMemory<InputRecord> Records { get; }
+    public ulong NextSequence { get; }
+    public bool HasGap { get; }
+}
```

不明な DeviceId は KeyNotFoundException、不正な enum・負の保持設定・現在のシステム Sequence を超えるカーソルや削除境界は ArgumentOutOfRangeException とする。Key.Unknown の押下照会は false とする。公開スナップショットは内部の書き換え可能な配列を露出せず、その後の更新・削除でも内容を変えない。

### 参照・通知・ポーリングの共通契約

1. 全 Source の Update を注入順に呼び、Registry の登録・削除要求を収集する。新規 Device を登録し、全 Device の DrainEvents をデバイス ID 順に呼んでバッチを検証する。削除要求のある Device も残った入力を最終取得する。
2. 各デバイスのイベント順に状態を計算し、デバイス別配列に記録を追加する。フォーカス喪失・切断による合成解放も同じ経路で追加する。
3. 新規登録には接続記録を先行させ、デバイスごとに検証済みバッチの配列と状態を公開する。削除要求のある Device は最終入力と切断・合成解放を記録してアクティブ一覧から削除する。
4. 新規記録を Recorded で Sequence 順に通知する。通知中の配列参照は今回のバッチまでを含み、状態照会はバッチ終了時点の状態を返す。
5. 通知完了後、件数・時間の保持方針を評価して古い履歴を削除する。Manual では削除しない。

複数デバイス間の Sequence は Source の注入順・Device の ID 順による取り込み順を表し、OS 上の厳密な発生順とは扱わない。各デバイス内の受信順は維持する。

読み取りや通知は記録を消費せず、別の利用者のカーソルや結果を変更しない。通知時点の状態は各記録直後の中間状態ではないため、遷移順を必要とする購読者は通知の Data を使用する。保持期間をゼロにしても新規記録は一度通知するが、Update が戻った後の履歴は空になり得る。

GetRecords は呼び出し時点の配列を読み取り専用メモリとして直接提供する。取得済みのスナップショットは削除後も有効とするため、削除は InputSystem が保持する参照を解放する操作であり、利用者が保持する配列まで強制回収するものではない。

ReadRecords はデバイスごとに Sequence > afterSequence の記録を返す。NextSequence は今回の取得時点のシステム全体の最終 Sequence とし、該当記録が空でもその値へ進められる。利用者は返却された NextSequence を次回に渡す。0 はシステム開始からの取得を意味する。

各デバイスは最後に削除した記録の Sequence を保持する。これが afterSequence より大きければ HasGap を true とし、保持期間の超過や明示削除で未読記録を失ったことを通知する。欠落を黙って完全な履歴として返さない。利用者は現在状態へ同期するか、取得不能をエラーとして扱うかを選ぶ。

押下・解放を前後状態の差だけで判断しない。同じ取得バッチ内の短い押下→解放も二つの記録として残る。WasPressed のようなフレーム限定フラグはこの層の主要契約にせず、利用者がカーソルで選んだ期間の記録から求める。リピートと重複イベントは記録に残すが、重複した押下・解放で状態遷移を再発生させない。

### 保持方針と削除時点

| 方針 | 設定・操作 | 削除時点 |
| --- | --- | --- |
| 手動 | MaxRecords / MaxAge ともに null | 自動削除なし。利用者が RemoveRecordsThrough または ClearRecords を呼ぶ |
| 件数 | MaxRecords を設定 | Update の通知完了後、最新 N 件を残して先頭から削除 |
| 時間 | MaxAge を設定 | Update の通知完了後、現在の単調時刻との差が上限以上の記録を削除 |
| 件数＋時間 | 両方設定 | どちらかの制限を超えた古い記録を削除 |
| 利用者の処理区切り | Prune、RemoveRecordsThrough、ClearRecords | ゲーム更新・固定時間ステップ・保存完了など、利用者が選んだ時点 |

Prune は設定済みの件数・時間制限だけを評価し、Manual の記録を消さない。入力が届かない Update でも時間制限を評価する。Update を呼ばない期間は自動削除も動かないため、必要なら利用者が Prune を呼ぶ。バックグラウンドタイマーは設けない。

初期実装は既定の保持方針を Manual とする。履歴が際限なく増えるため、長時間動作する利用者は明示削除または上限設定を行う。複数の利用者がすべて読み終えてから消す場合は、そのデバイスに対する各カーソルの最小値まで RemoveRecordsThrough を呼ぶ。InputSystem は利用者の処理完了を推測しない。

削除は現在状態、通知済みかどうか、ID や Sequence に影響しない。切断済みデバイスの記録にも同じ保持方針を適用する。デバイス情報と削除済み位置はシステムの生存期間中維持し、切断後もポーリングと欠落検出を可能にする。

### 正規化・フォーカス・切断

物理キーは US 配列におけるキー位置で表し、文字入力と区別する。OS 独自の整数コードを共通 API に流さない。マウス位置は対象領域左上を原点、右を X 正、下を Y 正とする DPI 調整済みの論理ピクセルとし、Browser では CSS ピクセルを使う。ホイールは右を X 正、上を Y 正、一段を 1 とし、小数を保持する。完全なキー対応と環境別ホイール換算はバックエンド ADR で定める。

Source は接続時に IInputDevice を RegisterDevice へ渡し、切断時に UnregisterDevice を呼ぶ。InputSystem が DeviceConnectedData / DeviceDisconnectedData を生成し、Device は初期 FocusData と通常入力を提供する。フォーカス喪失または切断時は、そのデバイスで押下中のキー・ボタンを解放する記録を状態変更の直後に enum 値順で合成する。履歴は消さない。

非フォーカス中の通常入力は記録・状態に適用しない。復帰時は新たな非リピート押下を待ち、リピートだけで解放状態を復活させない。マウス位置は最後の値を保持し、復帰後最初の位置を移動量計算の新しい基準とする。履歴から移動量を集計する利用者はフォーカス・接続境界をまたいだ位置差を加算しない。

コントローラーは識別可能な接続ごとに別デバイスとして登録し、接続・切断を記録する。Browser の接続スロットも接続期間ごとに新しい ID を割り当てる。OS API が状態ポーリングを提供する場合は、Platform が前回取得値との差をボタン・スティック・トリガーの共通データに変換する。初回は全ボタン解放・アナログ値 0 を基準とし、値が変わった項目を通知する。取得間隔より短い操作は復元できないため、イベント取得型とは取得精度が異なることを明記する。

Platform は標準ゲームパッドの物理的な位置に共通ボタンを割り当てる。非標準配置や未対応の操作は推測せず、対応表・未対応範囲をバックエンド ADR に記載する。スティックの各軸を -1〜1、トリガーを 0〜1 に正規化し、非有限値を拒否する。デッドゾーン、反応曲線、トリガーからボタンへの変換は利用者側の処理とし、記録する正規化値を変更しない。

フォーカス喪失・切断時はコントローラーボタンも合成解放し、非ゼロのスティック・トリガーには値 0 の記録を追加して現在状態を中立にする。復帰時に押し続けたボタンを新たな押下として扱わないよう、Platform は一度解放されるまで押下通知を抑制する。アナログ入力は復帰後の新しい取得値から記録を再開する。

物理デバイス識別不能な環境では同種入力の混在を論理デバイスの履歴として扱う。独立した複数キーボード等の機能を提供できると偽らず、Platform ごとの識別粒度を明示する。

### タッチ・ペン・筆圧

Touch のデバイス ID と指の ContactId は別の識別子とする。一つのタッチデバイス内で複数の接触を同時に記録する。Platform は OS の接触 ID を接触期間ごとの新しい ContactId に変換し、Began → Moved（0 回以上）→ Ended または Canceled の順序を保証する。筆圧だけの変化も Moved として記録する。終了記録を履歴へ追加してから最新状態の TouchContacts から除去する。

Pen はデバイスごとに PointerId を持ち、Entered → Moved / Down / Up → Left を扱う。Down / Up は筆先の接触開始・終了、Moved は位置・筆圧・ボタン等の変化、Left は検出範囲からの離脱、Canceled は入力の中断を表す。Up 後もホバー可能なため PenPointers に残し、Left / Canceled で除去する。接触状態は各 PenData の IsInContact で参照でき、Down は true、Up / Left / Canceled は false とする。Moved はその時点の接触状態を保持する。ホバーを取得できない環境では Platform が最初の Down 前に Entered、Up 後に Left を補う。消しゴム側は IsEraser、側面ボタンは PenButtons で表す。

位置はマウスと同じ対象領域の論理座標とし、筆圧はタッチ・ペンとも 0〜1 の正規化値とする。未取得・非対応は null とし、実測値 0 と区別する。Platform は各機器の圧力範囲を換算し、有限値かつ範囲内の値だけを提供する。既定の筆圧カーブや平滑化は適用せず、描画側が選べるようにする。Pen の Up とホバー時は筆圧対応機器なら 0、非対応なら null とする。筆圧対応の有無をブラシ側が確認できる。

フォーカス喪失、デバイス切断、OS のキャンセルでは、アクティブな接触・ペンを ID 順に Canceled として記録し、最新状態から除去する。合成キャンセルには最後の位置と、対応機器なら筆圧 0、非対応なら null を使用し、Pen の IsInContact は false、PenButtons は None とする。復帰後に古い接触を復元せず、新しい接触開始を待つ。ブラウザーの既定ジェスチャーによるキャンセルも Ended と区別する。

Source はタッチ・ペンの識別粒度に従って Device を登録する。OS が一つの画面や入力対象にまとめて提供する場合は Logical とする。タッチ・ペン由来の互換マウス入力は Platform が識別できる範囲で除外し、同じ操作を二重記録しない。識別不能な場合はバックエンドの制約を明記する。ジェスチャー認識、傾き・回転・接触面積の拡張は後続 ADR とする。

### スレッド・所有権・エラー

構築・破棄、Update、Registry 操作、配列取得、状態照会、ポーリング、保持設定、履歴削除は InputSystem の管理スレッドから呼ぶ。Update と通知への再入を禁止し、通知中は参照・状態照会・ポーリングのみ許可する。保持設定・削除・再更新は禁止し、InvalidOperationException を返す。購読の追加・解除はその時点以降の通知に反映し、通知中の呼び出し先一覧は固定する。

別スレッドからの入力は Device が受信キューを同期する。接続・切断コールバックは Source がキューに蓄積し、管理スレッドで Registry を呼ぶ。入力対象のイベントスレッドが異なる場合も、Platform 側で管理スレッドへ橋渡しする。別スレッドの利用者には、所有側が取得済みの不変配列・状態を同期して引き渡す。InputSystem 自体は並行アクセスを保証しない。

InputSystem は注入された Source を借用し、RegisterDevice 成功後の Device の所有者とする。Source がデバイスを見つけ、Source 専用の Registry が登録元を記録する。他 Source のデバイス削除、同じ Source・Device インスタンスの重複登録、同じ物理入力を複数 Source から登録する構成は許可しない。最後の構成は Platform / Engine が排他的なソース選択で防ぎ、共通層が OS の物理識別を推測して統合しない。

Registry の削除要求は管理操作として保留し、Update の最終入力・切断・合成解放の公開と通知後に Device.Dispose を一度だけ呼ぶ。Devices から削除しても、DeviceInfos、現在状態の最終値、履歴、削除済み位置は保持方針に従って参照できる。Device の登録削除と履歴削除を区別する。

InputSystem.Dispose は全 Source を注入の逆順で Shutdown して接続監視を止め、所属 Device の最終取得・切断・合成解放を通知して破棄する。Shutdown は Device を独自に破棄したり、未処理入力を消したりしない。すべての Registry を無効化した後、DI スコープまたは構成側が Source.Dispose を呼ぶ。解放中の例外でも他のリソースの解放を続け、最後にまとめて報告する。取得済み配列は有効だが、破棄済み InputSystem の照会・更新は ObjectDisposedException とする。DI 構成は InputSystem を Source より先に終了させる。

コンストラクターは列挙の検証後に各 Source.Initialize を注入順に呼ぶ。初期化中の Device 登録は暫定扱いとし、すべて成功してから確定する。Initialize が失敗した場合は、呼び出しを開始した Source を逆順に Shutdown し、暫定登録した Device を InputSystem が破棄する。Registry は無効化し、Source 自体は破棄しない。Source は初期化途中でも Shutdown で監視を解除できるようにする。元のエラーと後処理のエラーはまとめて構成側に報告する。

Source は登録済み Device を独自に破棄しない。Registry は Source の初期化・更新の呼び出し中だけ操作可能とし、通知中や切り離し後の呼び出しを拒否する。登録に失敗した Device は Source が所有したままとする。

Device は DrainEvents の失敗時に入力を消費しない。取得例外はデバイスごとに収集し、そのデバイスの既存状態・履歴は維持する。他のソース・デバイスの処理は継続し、Update は処理と通知・保持評価の完了後に AggregateException を返す。Source.Update の例外も収集し、成功した登録・削除要求は処理する。ほかの Source と既存 Device の取得は継続する。デバイス間では全体ロールバックを保証しない。不正な入力バッチも該当デバイス単位で拒否し、自動再試行しない。所有者は障害のあるデバイスや Source を停止・修復する。削除中の最終取得が失敗しても、欠落エラーを報告して切断・中立化とリソース解放を続行する。

購読者の例外は記録成功を巻き戻さない。ほかの購読者と後続記録の通知を継続し、保持方針の評価後に AggregateException として報告する。この場合、記録と通知処理は完了しており、Update の再試行で同じ記録を再配信しない。バックエンドの受信キュー制限による欠落と、利用者設定による履歴削除は区別し、前者を黙って捨てない。

## 検討した代替案

### 毎フレーム履歴を一律に削除する

保持メモリは小さくなるが、処理周期が異なる利用者がデータを取得できない。取得周期と保持方針を分離する。

### デバイスを種類ごとに常に統合する

実装は単純だが、識別可能な複数デバイスを選べなくなる。物理識別できる環境では分離し、取得できない場合だけ論理デバイスとして提供する。

### 配列参照・イベント・ポーリングに別の記録を持つ

方式ごとに順序、寿命、内容がずれる可能性がある。一つのデバイス別記録を共通の基盤にする。

### 読み取り時に記録を消費する

単一の利用者には便利だが、ゲームと UI、保存処理などが互いの入力を奪う。非破壊の読み取りと利用者別カーソルを採用する。

## 結果と影響

- 利用者はデバイス別の配列参照、通知、ポーリングを併用できる。
- 履歴を消すタイミングと期間を処理周期に合わせて設定できる。
- 履歴を削除しても最新状態は照会でき、未読履歴の欠落はカーソルで検出できる。
- Manual の長期利用、切断済みデバイス情報、保持された配列にメモリを使用する。利用者が削除と参照解放を管理する必要がある。
- 不変配列の公開にはコピー等のコストがある。最初は寿命の明確さを優先し、性能測定後に内部表現を最適化する。
- 物理デバイスを分離できる範囲は Platform の入力 API に依存する。

## 検証方針

実装時に偽の Source・Device と TimeProvider を使い、次を検証する。本 PR は文書のみであり、実行テストを追加しない。

- 複数 Source が Device を登録・削除でき、全 Source を InputSystem が更新する。
- 全 Source 間で ID が一意となり、再接続で再利用せず、他 Source の登録削除を拒否する。
- DI で複数 Source を受け取り、一度だけ列挙して注入順に管理する。空、null、重複、初期化失敗の契約を検証する。
- Device 削除・システム破棄で最終記録と合成解放を残し、Device を一度だけ解放する。
- InputSystem が借用 Source を破棄せず、Shutdown と DI スコープの Dispose の順序が正しい。
- 二つ以上のデバイスの配列・状態が混ざらない。
- 配列、通知、ポーリングの DeviceId・Sequence・時刻・データが一致する。
- 短い押下、リピート、同一バッチ内の複数遷移が記録に残る。
- 一利用者の読み取りが別利用者を消費せず、空取得も含めカーソルが正しく進む。
- Manual、件数、時間、併用、上限 0、入力なしの更新、設定変更、明示削除が契約どおりに動作する。
- 未読履歴の削除で HasGap が立ち、読了範囲の削除では立たない。
- 削除で現在状態を変えず、取得済み配列も変えない。
- フォーカス喪失・切断で合成解放を全方式から参照できる。
- 複数のタッチ接触を個別に記録し、終了・キャンセル・OS ID 再利用で混同しない。
- ペンのホバー・接触・離脱・消しゴム・側面ボタンと、筆圧 0 / null / 範囲外を正しく扱う。
- 筆圧だけの変化と合成キャンセルが配列・通知・ポーリングに一致し、最新状態を残さない。
- 複数コントローラーの識別、ボタンの短い押下、スティック・トリガーの範囲・向き・初期値・中立復帰を検証する。
- 状態取得型バックエンドで値の変化を記録し、復帰時の押下抑制と接続スロットの再利用を正しく扱う。
- 通知例外・再入禁止・不正バッチ・Source / Device 例外で記録や通知が重複しない。

実環境では Windows / Linux / Browser のデバイス識別粒度、物理キー、DPI、ホイール、コントローラーのボタン対応・アナログ正規化、マルチタッチ・ペン筆圧・キャンセル・互換マウス入力、フォーカスと切断を検証する。

## 別途決定する事項

- 各 Platform の入力ライブラリ、物理デバイス識別方法、Native 境界、Browser の DOM 連携。
- キー enum の完全な定義と変換表、ホイール換算、マウスキャプチャ・ポインターロック。
- 各環境のコントローラー取得 API と非標準配置の対応表、振動・ハプティクスなどの出力機能。
- タッチ・ペンの各環境の取得 API、傾き・回転・接触面積、ジェスチャー認識。
- 文字入力・IME、アクションマッピングと UI の入力伝播。
- OS 発生時刻の取り扱い、永続記録・リプレイ形式、固定時間ステップへのイベント分配。
- 計測に基づく性能目標、受信キュー上限、長期稼働時の切断済みデバイス情報の回収。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
