# ADR-INPUT-0002: デバイスデータ補正と仮想デバイス生成

- 状態: 提案
- 日付: 2026-10-09

## 背景

[Input 基盤](INPUT-0001-input-system.md)は正規化されたデバイス別記録を提供する。ゲームではデッドゾーンや筆圧カーブ、操作の先行受付、ピンチや長押し等が必要となる。これらを利用者ごとに実装すると、時間判定・キャンセル・履歴欠落への対応が分散する。

## 決定

### 加工の区分と配置

加工を四つに分ける。本 ADR はマッピング前の二つを扱い、後の二つは [アクション層](INPUT-0003-actions-and-contexts.md)で決定する。

| 区分 | 入出力 | 責務と例 |
| --- | --- | --- |
| デバイスデータ補正 | Device の InputData → 補正済み InputData | 中心ずれ、デッドゾーン、筆圧校正、座標変換 |
| 仮想デバイス生成 | 一つ以上の Device のデータ → 新しい IInputDevice | タッチから仮想スティック、ピンチ・回転から軸、複数機器の統合 |
| アクション値補正 | マッピングした値 → 補正したアクション値 | 移動の正規化、照準感度、ゲーム側の反応曲線 |
| マッピング後の操作認識 | アクションの値・遷移 → 操作イベント | 決定の長押し、攻撃の連打、コマンド入力 |

`src/Platform/Lumyte.Input.Processing/`、名前空間・パッケージ名 `Lumyte.Input.Processing` にデバイス側の処理を置く。依存は `Lumyte.Input` と .NET のみとし、Actions に依存しない。アクション名やコンテキストを入力にしない。

### デバイスへの適用と元データ

補正処理は Device から取得した正規化済み InputData を直接受け取り、同じ入力種別のデータを返す。アクションへの割り当て前に適用する。Source は補正する Device をデコレーターで包み、Registry に登録できる。ラッパーは元 Device の DrainEvents を一度だけ呼び、取得・破棄の所有権を一つにする。元 Device とラッパーを重複登録しない。

元データを不変のまま補正済みコピーを生成する。取得時に元バッチを診断用の分岐へ渡せるようにし、元データ保存を必要とする利用者は明示的に記録する。InputSystem に登録した補正済み Device の履歴は補正後のデータとなる。アクション側の追加加工はその履歴を変更しない。

補正設定は順序付き不変設定として保持し、デバイス補正とアクション補正を別の設定欄で管理する。同じデッドゾーン等を暗黙に二重適用しない。設定変更で平滑化等をリセットし、接触中の座標変換変更はキャンセル境界を入れて次の接触から反映する。

### デバイスデータ補正

処理順は正規化値 → デッドゾーン → 機器校正用の反応曲線 → 必要なら平滑化とする。設定は登録時にコピーして固定し、元記録を変更しない。スティックは円形デッドゾーンを既定とし、半径 d 以下をゼロ、残りを `(r-d)/(1-d)` に再写像して方向を維持する。対角方向の半径は 1 にクランプする。

トリガーと筆圧は 0〜1 の範囲を維持し、非対応の筆圧 null を 0 に置き換えない。反応曲線は有限・単調増加、端点 0 / 1 を保証する。d は 0 以上 1 未満、ゲームの照準感度はアクション側に置く。平滑化は明示的に有効化し、単調時間差による指数平滑化を使う。フォーカス喪失・切断・キャンセル時は中立化し、平滑化の前回値を捨てる。

### 仮想デバイスとマッピング前のジェスチャー

仮想デバイスは IInputDevice を実装し、DI で注入する仮想 IInputSource が Registry から登録・削除する。新しい InputDeviceId と Logical の識別粒度を持ち、元デバイスとの関連を保持する。マッピングとリバインドからは通常のデバイスとして選択できる。

初期出力は既存の ControllerButtonData / ControllerStickData / ControllerTriggerData に投影する。例えばタッチスティックは Left スティック、ピンチ比と回転角は設定された基準・範囲で Right スティックの二軸へ変換する。出力の意味・範囲は仮想デバイスの設定として明示する。任意の新しい軸・コントロールを追加する基盤拡張は別途決定する。

| 認識 | 契約 |
| --- | --- |
| スワイプ | 一接触の開始から正常終了までの距離・期間・方向で判定。キャンセルでは成立しない |
| ピンチ・回転 | 同一デバイスの二接触を ID で固定し、距離比・角度差を算出。接触終了で中立化し、新しい組で基準を再設定 |
| タッチスティック | 接触開始位置を中心として移動を軸へ変換し、終了・キャンセルでゼロに戻す |

接触情報が必要な認識はマッピング前で行い、出力を仮想デバイスにする。長押し・連打・同時押し・順序入力は原則としてマッピング後のアクションを認識する。時間・しきい値判定等の共通アルゴリズムは再利用できるが、デバイス ID とアクション ID の状態管理は混ぜない。

### 更新順と派生デバイスの安全性

処理順は実デバイス取得 → デバイス補正 → 仮想デバイス生成 → アクションマッピングとする。補正されたバッチを共有する取得コーディネーターが一度だけ取得し、実 Device と派生 Device の DrainEvents はそれぞれの準備済みキューを返す。Source.Update では接続監視と取得準備を行い、実 Source の接続更新より後にコーディネーターを実行する依存順を構成側で保証し、マッピングは InputSystem.Update が完了してから開始する。

仮想 Device が同じ InputSystem の ReadRecords を読んで同じ更新へ戻す構成は使用しない。現在の Source 更新 → Device 取得の順序では前回分を読むことになるためである。過去履歴を読む独立したリプレイ用途は更新遅延を明示する。

派生関係を DAG とし、元デバイスから順に評価する。自己参照・循環は構成時に拒否する。元デバイスの切断・フォーカス喪失・キャンセル・データ欠落時は派生出力も中立化し、必要なら仮想 Source が Device を登録解除する。元と派生が同じアクションへ同時に割り当てられる場合は、アクション層の合成・競合規則を適用する。

### 公開 API 一覧

新規 API の宣言案。設定型と本体の詳細は実装前に具体化する。

```diff
+public interface IDeviceDataProcessor
+{
+    // Device から取得したデータを補正し、元バッチは変更しない。
+    IReadOnlyList<InputData> Process(
+        IReadOnlyList<InputData> data, TimeSpan now);
+    void Reset();
+}
+public sealed class CorrectedInputDevice : IInputDevice
+{
+    // 元 Device の所有権を引き受け、取得・破棄を一度だけ実行する。
+    public CorrectedInputDevice(
+        IInputDevice device, IReadOnlyList<IDeviceDataProcessor> processors,
+        Func<TimeSpan> getTime);
+    public InputDeviceDescriptor Descriptor { get; }
+    public IReadOnlyList<InputData> DrainEvents();
+    public void Dispose();
+}
+public interface IVirtualDeviceGenerator
+{
+    // 元 Device とコーディネーターが同じ更新時間軸を共有する。
+    IReadOnlyList<InputData> Generate(
+        InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now);
+    // 中断を解放・ゼロ値・キャンセルへ変換する。
+    IReadOnlyList<InputData> Reset(InputDeviceId origin, TimeSpan now);
+}
```

now はコーディネーターが共有する単調経過時間とし、逆行を拒否する。空バッチでも時間依存加工を進められる。InputDeviceId が必要な派生関係は元 Device の登録確定後に接続する。元データ分岐、コーディネーター、仮想 Source / Device の完全な API と時刻の受け渡しは実装前に具体化する。

### InputSystem との相互作用とサンプル

以下は構成と呼び出し順を示す設計用サンプルである。Processing、Platform とコーディネーターの具体クラスは未実装であり、コンパイル可能なサンプルプロジェクトではない。記載する InputSystem / Registry の既存 API と、追加提案を区別する。

#### 共通時間軸のための基盤 API 追加案

補正・仮想生成・アクション認識は InputSystem.RecordedAt と同じ時間軸を使う。現在の InputSystem は開始時刻を公開していないため、次の読み取り専用 API を追加提案する。同じ TimeProvider を渡すだけでは開始時刻が一致しないので、各層で独立した Stopwatch を開始しない。

```diff
+// InputSystem 内部の開始timestampから、注入済み時計で計算する。
+public TimeSpan InputSystem.ElapsedTime { get; }
```

#### Device 入力を補正して登録する

Source は接続を検出したとき、元 Device をラップして Registry に登録する。登録失敗時のラッパー破棄は Source、登録成功後は InputSystem の責務となる。

```csharp
// IInputSource.Initialize / Update の中で実行するコード。
IInputDevice raw = platform.OpenController(connection);
var corrected = new CorrectedInputDevice(
    raw,
    new IDeviceDataProcessor[]
    {
        new StickCalibration(centerOffset), // 機器の中心ずれ
        new RadialDeadZone(0.15f),
    },
    getTime); // () => input.ElapsedTime。DrainEvents 時に評価する。

InputDeviceId id;
try
{
    id = registry.RegisterDevice(corrected);
}
catch
{
    corrected.Dispose(); // 未登録の raw も一度だけ破棄する。
    throw;
}
connections.Add(connection, id);

// 切断を検出した Source.Update 内で要求する。
registry.UnregisterDevice(connections[disconnectedConnection]);
connections.Remove(disconnectedConnection);
```

InputSystem.Update は Source.Update を呼び、登録済み corrected.DrainEvents を呼ぶ。ラッパーは raw の入力を一度だけ取得・補正する。InputSystem は補正済み入力に ID・Sequence・RecordedAt を付け、記録・現在状態・Recorded 通知へ反映する。切断要求では最終取り込みと中立化の後に corrected を破棄する。アクション側は通常の ReadRecords を使えばよく、補正処理を再実行しない。

#### タッチから仮想コントローラーを生成する

次の構成コードの型は、取得コーディネーターの責務を説明するための仮の名前であり、確定した公開 API ではない。

```csharp
// 診断・仮想生成に分岐するが、OS 入力の取得は一度だけ。
var acquisition = new SharedTouchAcquisition(platform, getTime);
var touchSource = new BufferedTouchSource(acquisition);
var virtualSource = new VirtualControllerSource(
    acquisition,
    new TouchStickGenerator(radius: 80f));

// touchSource.Update が接続を更新し、virtualSource.Update が
// 同じ取得バッチから生成する。各 Device は準備済みキューだけを返す。
input = new InputSystem(new IInputSource[] { touchSource, virtualSource }, clock);
input.Update();
```

touchSource が Touch Device を、virtualSource が Logical な Controller Device をそれぞれ Registry へ登録する。仮想 Source は元 Touch の登録済み ID を参照できる構成窓口を持つ。仮想 Device は ControllerStickData を返し、InputSystem は実デバイスと同じ経路で履歴・状態を生成する。入力途中のタッチキャンセルも同じバッチで仮想スティックのゼロ値に変換し、一更新遅れにしない。

共有取得の寿命は構成側が両 Source より長く保ち、Device の分岐を一つ閉じても残りを破棄しない。終了順は InputSystem → Source → acquisition とする。getTime は InputSystem の構築後に参照可能にし、Initialize では取得・時刻評価を行わず、Update から評価する。

### エラーとライフサイクル

管理スレッドで処理し、スナップショットだけを他スレッドへ渡す。不正な設定・非有限値・逆行時刻は拒否する。過去記録から再処理する利用者は Sequence と HasGap を検証し、欠落時に加工・ジェスチャー状態をリセットする。現在状態から未観測のジェスチャーを推測しない。

Source が登録 Device の所有権を InputSystem に渡し、ラッパーは内部の元 Device を所有する。共有取得の分岐 Device は元 Device を個別に破棄せず、コーディネーターが共有資源を一度だけ解放する。アクション層が Source / Device を直接破棄しない。

## 検討した代替案

- InputSystem に認識を組み込む: 利用者ごとに異なる加工設定や処理周期を持てなくなる。
- 現在状態だけで認識する: 短い押下と順序を失うため、記録と単調時間を使用する。
- すべてをアクション後に加工する: 接触IDや機器校正など、マッピングで失われる情報が必要な処理を扱えない。

## 結果と影響

機器固有の補正と操作の意味を分離し、仮想 Device を既存の登録・履歴・マッピングの仕組みに接続できる。元入力の保存は取得分岐として明示的に構成する必要がある。派生関係の更新順、共有取得と所有権の管理が追加される。

## 検証方針

デッドゾーン境界、筆圧 null、元バッチの不変性、一更新一取得、二接触の入替・キャンセル、派生 Device の登録・切断・中立化、循環拒否、元と派生の二重入力、資源の一度だけの破棄を検証する。

## 別途決定する事項

設定型と完全な公開 API、取得コーディネーター、仮想デバイスのコントロール表示・保存形式、複数接触の選択規則、性能目標は実装前に具体化する。本 PR は設計提案のみである。
