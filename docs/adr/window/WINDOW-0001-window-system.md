# ADR-WINDOW-0001: 複数ウインドウと一時表示の共通管理

- 状態: 提案
- 日付: 2026-10-10
- 更新日: 2026-10-11

## 背景

ゲームのメイン画面、独立したツール画面、ポップアップ、コンテキストメニュー、IME の候補一覧は、表示領域の生成・配置・前後関係・入力対象・終了処理を必要とする。一時表示を個別に実装すると、親画面の終了後も表示が残る、メニュー操作で入力フォーカスを失う、DPI の異なる画面で位置がずれるといった問題が起きる。

Lumyte は Windows、Linux、Browser を対象とする。OS の独立ウインドウと、一つの画面内に描画する領域では可能な制御が異なる。特に Wayland のトップレベル位置指定、Browser の独立画面生成、OS が管理する IME 候補一覧を、すべて同じネイティブ機能として保証できない。

[Input の設計](../input/INPUT-0001-input-system.md)はデバイスごとの記録を担当し、文字入力・IME・UI への伝播を別途設計する方針である。Graphics の提示は採用済みの [Surface・Swapchain・Present 契約](../graphics/GRAPHICS-0013-surface-swapchain-presentation.md)を使用する。この ADR で Window の境界と両者の接続条件を決める。

本 PR は設計文書のみを追加する。以下の API は未実装の提案であり、実装済みの機能や実機での対応を表さない。

## 決定

### 管理単位と配置

共通ライブラリを `src/Platform/Lumyte.Windowing/` に配置し、プロジェクト・NuGet・名前空間を `Lumyte.Windowing` とする。`WindowSystem` は構成側が注入する一つの `IWindowBackend` を借用し、作成した全 Window を所有する。複数の独立画面も同じ System で管理する。ヘッドレスでは Native 作成を拒否するバックエンドを注入できる。

Window は次の二つの実体を明示的に区別する。

- `Native`: OS またはブラウザーが提供する独立した表示先。親のない Native は画面提示のルートとなる。所有者付き Native は別の表示先を持ち、親は Native に限定する。
- `Hosted`: 親 Window の表示先に描画する領域。必ず親を持ち、最も近い Native 祖先の表示先を共有する。共通層が配置・可視性・重なり順・ヒットテストを管理する。

役割は `Normal`、`Tool`、`Popup`、`ContextMenu`、`ImeCandidates` とする。役割は OS の実体と独立しており、例えばコンテキストメニューを Native または Hosted で表示できる。自動的に実体を切り替えない。要求した Native 機能がなければ作成を拒否し、利用側が別の Descriptor で Hosted を選ぶ。

Hosted は UI ウィジェットの代替ではない。メニュー項目、候補選択、レイアウト、装飾、アクセシビリティツリー、アニメーションは UI 側が実装する。Window はその表示境界と入力配送先を提供する。

### 責務と依存関係

| 構成要素 | 責務 | 依存先 |
| --- | --- | --- |
| `Lumyte.Windowing` | ID、親子関係、状態、Hosted 配置、フォーカス方針、一時表示の寿命、通知 | .NET 標準ライブラリのみ |
| `Lumyte.Platform.Windows` / `Linux` / `Browser` | Native の生成・変更・イベント取得、座標変換、機能報告、OS の文字入力接続 | Windowing と Input の共通契約 |
| Platform の共有イベント取得器 | OS キューを一度だけ取得し、Window・Input・文字入力の各キューに振り分ける | 対象 Platform |
| `Lumyte.Windowing.Graphics` の標準アダプター | 全対象環境の標準構成で Native を Graphics Surface へ接続する唯一の公式入口 | Windowing、標準 Platform、Graphics.Abstractions、Wgpu / Browser |
| Engine / 利用側の統合コード | 更新順、入力配送、UI の閉じる方針、提示の設定・同期・終了順、標準外の直接接続 | 使用する共通契約とバックエンド |
| UI / テキスト編集 | Hosted の内容描画、入力消費、文字編集、メニューや IME 候補の操作 | 共通契約と統合アダプター |

Windowing は Input、Graphics、Engine、特定の DI コンテナーに依存しない。Input の既存公開型へ WindowId を追加しない。複数 Window 間の入力配送に使うメタデータは、Platform の取得時点で保持する別の入力 envelope として後続の入力配送設計で具体化する。

一つの OS イベントループを WindowSystem と IInputSource がそれぞれ破壊的に取得してはならない。共有取得器は一度取得し、各契約へ独立したキューを提供する。Native コールバック内でゲームや UI の購読者を直接呼ばない。

### ID・状態・座標

`WindowId` は System 識別子と単調増加する番号の組み合わせとする。番号 0 と空の System 識別子は無効。閉じた ID は再利用せず、別 System の ID も拒否する。ID は永続化キーや OS ハンドルではない。Window の親と実体は作成後に変更しない。

`WindowState` は不変スナップショットで、要求値と観測値を区別する。例えば Show の要求が受理されても OS が表示を確定するまでは `IsVisible` は変わらない。`IsVisible` は Window 自身の表示状態、`IsEffectivelyVisible` は全祖先の表示・最小化も考慮した状態である。取得済みスナップショットを後から書き換えない。

座標は左上原点、右・下を正とする論理ピクセルで統一する。Native の `ClientSize` はクライアント領域の論理サイズ、`PixelSize` は実際の描画先の整数ピクセル寸法、`Scale` は X/Y ごとの倍率とする。OS 枠のサイズは含めない。Hosted の `Bounds` は親のクライアント座標であり、描画時は祖先のクリップを交差する。Native の `Bounds` は `(0, 0, ClientSize.X, ClientSize.Y)` とし、画面上の位置を混在させない。

画面上の絶対座標は共通の通常操作として要求しない。所有者付き Native の配置だけ、所有者のクライアント座標にあるアンカーからバックエンドが変換する。Wayland 等で取れない絶対位置を推測しない。スケール変更と PixelSize 変更は同じ状態イベントで公開し、端数丸めによる実寸は PixelSize を正とする。サイズ 0、最小化、非表示時は提示を停止し、復帰後に再構成する。

### 公開 API 案

比較元は `main` の `cc766f2`。Windowing は存在しないため、以下はすべて追加宣言である。コメントに契約と既定値を示す。列挙と主要メンバーの抜粋であり、実装時の内部型は含めない。

```diff
+namespace Lumyte.Windowing;
+
+// 空の SystemId / Value 0 は無効。他 System の ID は受け付けない。
+public readonly record struct WindowId(Guid SystemId, ulong Value);
+public readonly record struct WindowRequestId(ulong Value);
+public readonly record struct WindowRect(float X, float Y, float Width, float Height);
+public readonly record struct WindowPixelSize(int Width, int Height);
+public enum WindowRealization { Native, Hosted }
+public enum WindowRole { Normal, Tool, Popup, ContextMenu, ImeCandidates }
+public enum WindowActivation { Activate, PreserveFocus }
+public enum WindowDisplayMode { Normal, Minimized, Maximized, Fullscreen }
+public enum WindowPlacementSide { Below, Above, Right, Left }
+public enum WindowRequestStatus { Applied, Denied, Unsupported, Failed, Superseded }
+public enum WindowCloseReason { Application, User, OwnerClosed, BackendLost, Shutdown }
+
+[Flags]
+public enum WindowFeatures
+{
+    None = 0, Native = 1, OwnedNative = 2, NonActivatingNative = 4,
+    NativeAnchorPlacement = 8, Minimize = 16, Maximize = 32, Fullscreen = 64,
+}
+
+// Anchor は親クライアント座標。Gap >= 0。反転後に可視範囲へ収める。
+public sealed record WindowPlacement(
+    WindowRect Anchor, WindowPlacementSide PreferredSide, float Gap = 0);
+
+// 生成は常に非表示。Parent の所属・寿命、実体と機能を事前検証する。
+public sealed record WindowDescriptor
+{
+    public string Title { get; init; } = "";
+    public WindowRole Role { get; init; } = WindowRole.Normal;
+    public WindowRealization Realization { get; init; } = WindowRealization.Native;
+    public WindowId? Parent { get; init; }
+    public System.Numerics.Vector2 ClientSize { get; init; } = new(800, 600);
+    // Hosted は必須。Native は null。Width/Height は ClientSize と一致させる。
+    public WindowRect? HostedBounds { get; init; }
+    // Popup / ContextMenu / ImeCandidates は PreserveFocus を必須とする。
+    public WindowActivation Activation { get; init; } = WindowActivation.Activate;
+    public bool IsResizable { get; init; } = true;
+}
+
+// WindowSystem が生成する観測値。Hosted の PixelSize/Scale は祖先から算出。
+public sealed record WindowState
+{
+    public WindowId Id { get; }
+    public WindowId? Parent { get; }
+    public WindowId PresentationRoot { get; }
+    public WindowRole Role { get; }
+    public WindowRealization Realization { get; }
+    public string Title { get; }
+    public WindowRect Bounds { get; }
+    public System.Numerics.Vector2 ClientSize { get; }
+    public WindowPixelSize PixelSize { get; }
+    public System.Numerics.Vector2 Scale { get; }
+    public bool IsVisible { get; }
+    public bool IsEffectivelyVisible { get; }
+    // Native の OS アクティブ状態。Hosted は PresentationRoot の値を継承。
+    public bool IsActive { get; }
+    public WindowDisplayMode DisplayMode { get; }
+    public WindowFeatures Features { get; }
+}
+
+public sealed class WindowSystem : IDisposable
+{
+    // Backend は借用。生成/操作/照会/Update/Dispose は作成スレッド限定。
+    public WindowSystem(IWindowBackend backend);
+    public WindowFeatures Features { get; }
+    public IReadOnlyList<WindowState> Windows { get; }
+    // 実際のキーボード配送先。OS が非アクティブな場合は null。
+    public WindowId? KeyboardTarget { get; }
+    // 生成失敗では公開せず Native リソースも巻き戻す。
+    public WindowId CreateWindow(WindowDescriptor descriptor);
+    public WindowState GetState(WindowId window);
+    // Native の表示先を型付きで借用する。Hosted は拒否し、祖先へ暗黙変換しない。
+    // TTarget が Backend の取得契約にない場合は NotSupportedException。
+    // 所有権は移らない。外部接続中は RetainNativeTarget で寿命を保持する。
+    public TTarget GetNativeTarget<TTarget>(WindowId window);
+    // Graphics に依存しない Native の寿命保持。Dispose は管理スレッドから呼ぶ。
+    // Close は論理終了するが、保持中の Native ハンドルの破棄を延期する。
+    public IDisposable RetainNativeTarget(WindowId window);
+    // Hosted の実効描画順、背面から前面。root 自身は含めない。
+    public IReadOnlyList<WindowId> GetHostedOrder(WindowId presentationRoot);
+    // root のクライアント座標。非表示・クリップ外を除外し最前面を返す。
+    // Hosted がなければ root、root 外または非表示なら null。
+    public WindowId? HitTest(WindowId presentationRoot, System.Numerics.Vector2 point);
+    public WindowRequestId Show(WindowId window);
+    public WindowRequestId Hide(WindowId window);
+    public WindowRequestId SetTitle(WindowId window, string title);
+    // Hosted は親座標の Bounds 更新。Native はクライアント寸法の要求。
+    public WindowRequestId SetBounds(WindowId window, WindowRect bounds);
+    // Parent 必須。配置機能を持たない Native では Unsupported を通知。
+    public WindowRequestId Place(WindowId window, WindowPlacement placement);
+    // PreserveFocus は拒否。OS のフォーカス取得は保証せず結果を通知する。
+    public WindowRequestId Activate(WindowId window);
+    public WindowRequestId BringToFront(WindowId window);
+    // Hosted は Normal のみ。Native は機能に応じて処理する。
+    public WindowRequestId SetDisplayMode(WindowId window, WindowDisplayMode mode);
+    // 直接の Close は強制終了。Native のユーザー要求だけ Closing で取消可能。
+    public WindowRequestId Close(WindowId window);
+    // 保留コマンドと開始時点の Backend イベントを順に取り込み、通知する。
+    public void Update();
+    public event Action<WindowEvent>? Changed;
+    public event EventHandler<WindowClosingEventArgs>? Closing;
+    // 保持中の Native があれば変更前に InvalidOperationException。
+    // それ以外は子から親の順に解放し、Backend 自体は Dispose しない。
+    public void Dispose();
+}
+
+// 不変通知。Sequence は System 全体で単調増加、1 起点。
+public sealed record WindowEvent(WindowId Window, ulong Sequence, WindowEventData Data);
+public abstract record WindowEventData { private protected WindowEventData(); }
+public sealed record WindowCreatedData(WindowState State) : WindowEventData;
+public sealed record WindowStateChangedData(WindowState State) : WindowEventData;
+public sealed record WindowKeyboardTargetChangedData(WindowId? Target) : WindowEventData;
+public sealed record WindowOrderChangedData(
+    WindowId PresentationRoot, IReadOnlyList<WindowId> HostedOrder) : WindowEventData;
+public sealed record WindowRequestCompletedData(
+    WindowRequestId Request, WindowRequestStatus Status, string? Error) : WindowEventData;
+public sealed record WindowClosedData(WindowCloseReason Reason) : WindowEventData;
+public sealed class WindowClosingEventArgs : EventArgs
+{
+    public WindowId Window { get; }
+    // このプロパティの設定だけ通知中に許可。他の操作は次の Update の前へ。
+    public bool Cancel { get; set; }
+}
```

すべての入力数値は有限値に限定し、生成時の寸法は正、SetBounds の寸法は 0 以上とする。負値、不正 enum、不整合な HostedBounds、Parent 不足、別 System の ID は `ArgumentException` 系、閉じた ID は `KeyNotFoundException`、破棄後は `ObjectDisposedException` とする。Native の SetBounds は X/Y が 0 の場合のみ許可し、移動要求は Place で表す。Popup / ContextMenu / ImeCandidates は Parent を必須とする。Native の親が Hosted なら拒否する。

`Features` はバックエンド全体の対応可能性、各 State の Features は当該 Window に適用可能な機能である。Native 作成に必要な機能がない場合は `NotSupportedException` とし、可視 Window や ID を一覧に公開しない。プラットフォームにより同期生成できない Native はバックエンド選定で追加の非同期生成契約を決める。本案ではその環境で同期 CreateWindow に対応したと表明しない。

操作は事前検証後にキューへ積み、RequestId を返す。RequestId は System 内で 1 起点、再利用しない。状態を即時に書き換えず、Update で Hosted に適用するか Native へ渡す。各要求は一度だけ RequestCompleted で完了する。OS が拒否・未対応・失敗した場合は対応する Status と診断文字列を通知し、要求値を観測状態へ反映しない。独立したユーザー操作による観測状態の変更は通常どおり取り込む。診断文字列に入力文字や候補内容を含めない。

Native の受理は完了を意味しない。バックエンドは観測された完了、拒否、または有限の期限内の失敗を返す。期限の具体値はバックエンド ADR で決める。後の要求が先の要求を置き換える場合は先に Superseded を通知する。終了時は未完了要求を Failed として完了させる。操作を同じ RequestId で自動再送しない。

### バックエンド境界

バックエンドには Native のみを渡す。Hosted は共通層が処理する。次は Platform 実装者向けの追加契約であり、ゲーム側の通常操作では使用しない。

```diff
+namespace Lumyte.Windowing;
+
+public interface IWindowBackend : IDisposable
+{
+    public WindowFeatures Features { get; }
+    // 隠した Native を生成。失敗時は部分生成リソースを Backend が解放する。
+    public WindowNativeState CreateNative(WindowId id, WindowDescriptor descriptor);
+    // 事前検証済みの Native コマンド。完了は DrainEvents で返す。
+    public void ExecuteNative(WindowNativeCommand command);
+    // 呼び出し開始時点のイベントを取得順で返す。新着は次回へ。
+    // OS キュー自体は Platform 共有取得器が処理する。
+    public IReadOnlyList<WindowBackendEvent> DrainEvents();
+    // 何度呼んでも安全。OS ハンドルの最終解放。通常終了時にも必須。
+    public void DestroyNative(WindowId window);
+}
+
+// Platform の Backend が実装する型付き取得契約。Graphics に依存しない。
+// TTarget の型・フィールドは Platform ごとに定義し、借用だけを返す。
+public interface IWindowNativeAccess<TTarget>
+{
+    public TTarget GetNativeTarget(WindowId window);
+}
+
+public abstract record WindowNativeCommand(WindowId Window, WindowRequestId Request);
+public sealed record ShowNative(WindowId Window, WindowRequestId Request)
+    : WindowNativeCommand(Window, Request);
+public sealed record HideNative(WindowId Window, WindowRequestId Request)
+    : WindowNativeCommand(Window, Request);
+public sealed record SetNativeTitle(WindowId Window, WindowRequestId Request, string Title)
+    : WindowNativeCommand(Window, Request);
+public sealed record ResizeNative(
+    WindowId Window, WindowRequestId Request, System.Numerics.Vector2 ClientSize)
+    : WindowNativeCommand(Window, Request);
+public sealed record PlaceNative(
+    WindowId Window, WindowRequestId Request, WindowPlacement Placement)
+    : WindowNativeCommand(Window, Request);
+public sealed record ActivateNative(WindowId Window, WindowRequestId Request)
+    : WindowNativeCommand(Window, Request);
+public sealed record RaiseNative(WindowId Window, WindowRequestId Request)
+    : WindowNativeCommand(Window, Request);
+public sealed record SetNativeDisplayMode(
+    WindowId Window, WindowRequestId Request, WindowDisplayMode Mode)
+    : WindowNativeCommand(Window, Request);
+
+// Backend の観測値。共通層が Parent/Role/ID 等と合わせ WindowState を生成。
+public sealed record WindowNativeState(
+    System.Numerics.Vector2 ClientSize, WindowPixelSize PixelSize,
+    System.Numerics.Vector2 Scale, bool IsVisible, bool IsActive,
+    WindowDisplayMode DisplayMode, WindowFeatures Features);
+public abstract record WindowBackendEvent(WindowId Window);
+public sealed record NativeStateChanged(WindowId Window, WindowNativeState State)
+    : WindowBackendEvent(Window);
+public sealed record NativeCloseRequested(WindowId Window) : WindowBackendEvent(Window);
+public sealed record NativeDestroyed(WindowId Window) : WindowBackendEvent(Window);
+public sealed record NativeRequestCompleted(
+    WindowId Window, WindowRequestId Request, WindowRequestStatus Status, string? Error)
+    : WindowBackendEvent(Window);
```

Close はキャンセル可能な OS 要求とは分離し、共通層が子を論理終了し、Native の寿命保持が解除されてから子・親の順に DestroyNative を呼ぶ。非表示・非活性での Show、所有者上の配置、Raise はその Window の PreserveFocus を守る。守れない Native 実装は NonActivatingNative を報告しない。

バックエンドはハンドルの再利用を WindowId の再利用に変換しない。閉じた ID への遅着イベントと、既に完了した RequestId の遅着完了は無視する。他 System のイベント、生成済みでない ID、不正な寸法やスケール、発行したことのない RequestId の完了はバックエンド契約違反として報告する。DrainEvents の失敗ではキューを消費しない。NativeDestroyed は取り消せない喪失であり、共通層は該当する子を強制終了する。

### 重なり順・フォーカス・入力配送

Native 全体の絶対的な Z 順は保証しない。OS のユーザー操作、他アプリ、Wayland の compositor が決める。BringToFront は要求と観測結果を分け、所有者付き Native は OS の所有関係を利用する。

Hosted の順序は PresentationRoot ごとに持ち、親より子を前面に置く。通常領域の層、その上の Popup / ContextMenu の層、最前面の ImeCandidates の層を設ける。同層では作成順、BringToFront で対象の部分木を前面へ移動する。子は親以上の層であることを生成時に検証する。異なる Native root 間で Hosted の順序を比較しない。透明な内容も WindowRect 全体を入力領域とし、内容ごとの透過ヒットテストは UI 側で扱う。

KeyboardTarget は OS の active root 内の最後に Activate が成功した Window とする。Hosted の Activate は可視性と Activation を検証して共通層で変更し、Native の Activate は実際の OS アクティブ化を待つ。active root を切り替えた場合は root ごとの最後の有効な対象を復元し、なければ root を対象にする。Hide / Close / 最小化で対象が無効になった場合は最も近い可視の Activate 可能な祖先へ戻す。OS のアプリ非アクティブ化では null とし、バックグラウンドへキーを配送しない。

Native / Hosted とも実効非表示の Activate は Denied とし、暗黙に Show しない。WindowKeyboardTargetChangedData の WindowEvent.Window は新しい対象、null への遷移では直前の対象とする。最初から対象が null なら変更通知を生成しない。

PreserveFocus の表示、Raise、ポインター操作で KeyboardTarget を変更しない。コンテキストメニューの矢印・Enter・Escape は UI の一時入力スコープが現在の KeyboardTarget の入力を消費する。候補一覧へのポインター操作も元の編集領域へ選択要求を渡す。WindowSystem はキーや候補選択を文字確定へ変換しない。

入力の最終配送先は OS の発生時点の Native root、同じ時点の Hosted 構造・座標・フォーカスを使う。Update 後の最終状態だけで過去の入力を再ヒットテストしない。共有取得器と統合アダプターは Window・Input の境界イベントを同じ順序で処理し、配送先を確定した envelope を UI へ渡す。その envelope の型と InputRecord との対応は後続 ADR で定義する。InputRecord.Sequence と WindowEvent.Sequence はそれぞれの System 内の順序であり、大小比較で統合しない。

アプリのフォーカス喪失は、Input ADR の対象デバイスの合成解放・接触キャンセルにつなぐ。Hosted 間の移動やアプリ内の別 Native への移動は物理デバイス切断と扱わず、古い UI 入力スコープの押下・ドラッグ・キャプチャをキャンセルする。ポインターキャプチャ・ロックの共通 API は後続の入力配送設計で決定する。

### ポップアップ・メニューの配置と寿命

Place は親のクライアント領域にある Anchor に PreferredSide と Gap を適用する。Hosted は root の可視クライアント領域、Native はバックエンドが取得できる利用可能な画面領域に収める。まず希望側、収まらなければ反対側、最後に位置をクランプする。領域より大きい場合はサイズを勝手に縮めず、Hosted はクリップ、Native はバックエンドの制約を結果に反映する。アンカーは最後の要求を保持し、親の移動・寸法・DPI 変更で再配置する。

Hide は対象の表示意図を false にし、子の表示意図を維持したまま実効表示を false にする。ただし Popup / ContextMenu / ImeCandidates の子孫は Close する。親の再表示で終了したメニューを自動復活させない。最小化とアプリのフォーカス喪失でも一時表示の子孫を Close する。Hosted の非表示はバックエンドを呼ばず、Native 子孫の実効非表示はバックエンドにも反映する。ネイティブ OS が非活性状態の候補ウインドウを維持できない場合も同じ終了経路を使用する。

Close は部分木を子から親へ論理終了する。通常の Tool も親に所有されていれば終了する。独立して存続する Tool は Parent を付けずに作る。終了時はテキスト入力・UI のキャプチャを解除し、新規描画を停止して Closed を一度だけ通知する。Native ハンドルは寿命保持の解除後に破棄する。GetState は論理終了後に失敗し、取得済み状態は参照可能である。

外側クリック、Escape、メニュー項目選択、サブメニュー開閉は UI の方針とする。UI は一時表示の部分木の内外を判定して Close を要求する。外側クリックを元の画面へ再配送するか消費するかも UI が一貫した方針を選び、閉じた Window へ遅れて配送しない。

### 文字入力と IME 候補一覧

Window が担当するのは文字入力の**表示先と幾何情報の接続**である。入力文字列、変換中の文字列・属性、確定文字列、候補内容のプロトコルは後続の文字入力 ADR で定める。キー入力から文字を合成しない。

テキスト編集領域は、Platform のテキスト入力セッションに対象 WindowId と、その Window のクライアント座標にあるキャレット矩形を渡す。Platform は PresentationRoot と各 Hosted の Bounds、Scale を使って OS が要求する座標へ変換する。Hosted のスクロール後は UI がキャレットを更新し、親の配置・DPI 変更でも再計算する。非表示またはフォーカス対象から外れた編集領域のセッションは終了し、終了したセッションからの遅着通知を無視する。終了時に変換を確定するかキャンセルするかは文字入力 ADR で明示する。

候補表示モードはセッション単位で次のどちらか一方にする。

- `Platform`: OS / ブラウザーが候補一覧を表示する。WindowSystem はこれを所有 Window として登録・破棄しない。キャレット位置など取得可能な情報だけ渡す。
- `Application`: Platform が候補の列挙と選択を提供し、標準候補 UI の抑止に成功した場合のみ使用する。UI は ImeCandidates / PreserveFocus の Hosted または対応する Native を生成し、候補を描画する。

Application はセッション開始前に対応可否を検査し、標準 UI の抑止が拒否された場合は失敗として返す。利用側が Platform を選び直すまで候補 Window を表示しない。候補一覧を取得できない環境で捏造した一覧を表示せず、同時に OS とエンジンの二つの候補一覧を出さない。変換終了・キャンセル・セッション変更で候補 Window を終了する。

候補選択はセッション ID と候補スナップショットの revision を伴って Platform へ要求し、古い候補 index を適用しない。確定文字列はテキスト入力の正式な通知だけから受け取る。Window の Changed 通知や診断には候補文字列を流さない。

### Graphics との接続と終了順

各 Native は独立した提示先を持ち、Hosted は PresentationRoot の提示先へ描画する。UI は GetHostedOrder と Bounds / IsEffectivelyVisible を使って描画する。Native 間の描画は独立させ、一つの最小化や提示失敗が他の画面を停止させない。

#### 公式アダプターは全環境を扱う一つに限定する

`src/Platform/Lumyte.Windowing.Graphics/` に標準アダプターを一つ提供する。パッケージと名前空間も `Lumyte.Windowing.Graphics` とする。「全環境」は Windows、Linux / X11、Linux / Wayland、Browser を意味し、Window バックエンドと Graphics バックエンドの全組み合わせを意味しない。

標準経路はデスクトップで `Lumyte.Graphics.Wgpu`、Browser で `Lumyte.Graphics.Browser` に固定する。環境ごとの表示先変換は同じアダプターの内部処理とし、OS × Window ライブラリ × Graphics API ごとの公開アダプター、登録用 factory、組み合わせ探索を追加しない。Browser とデスクトップの依存差は対象環境のビルドで切り替え、呼び出し側の入口は同じにする。Window 共通層と Graphics 共通層はこのパッケージに依存しない。

| 環境 | 標準 Window の提供値 | 標準 Graphics の接続先 |
| --- | --- | --- |
| Windows | HINSTANCE と HWND | Wgpu の `SurfaceSource.WindowsHwnd` |
| Linux / X11 | Display と Window | Wgpu の `SurfaceSource.XlibWindow` |
| Linux / Wayland | wl_display と wl_surface | Wgpu の `SurfaceSource.WaylandSurface` |
| Browser | canvas に対応する GPUCanvasContext | BrowserDevice の `CreateSurface(JSObject)` |

表示先は既存の生成済み Device へ渡す。Device の自動生成、別 Device への移し替え、Graphics バックエンドの自動選択は行わない。要求した Device と標準経路が適合しなければ接続を拒否する。全環境の対応は実装時の受け入れ条件であり、本設計 PR が実機対応済みであることは意味しない。

```diff
+namespace Lumyte.Windowing.Graphics;
+
+// 全対象環境で同じ入口を使用する。公式に提供する接続アダプターはこの一つ。
+public static class StandardWindowGraphicsAdapter
+{
+    // Native の借用表示先を標準 Graphics のバックエンド固有入口へ渡す。
+    // windows と device は借用。返す接続の所有者は呼び出し側。
+    // Hosted、不正 ID、終了済み Window は Window 共通契約に従って拒否。
+    // 標準外の組み合わせは NotSupportedException。黙って別経路を試さない。
+    // 失敗時は部分生成した Graphics 資源を解放し、Window は維持する。
+    public static WindowGraphicsConnection Connect(
+        Lumyte.Windowing.WindowSystem windows,
+        Lumyte.Windowing.WindowId nativeWindow,
+        Lumyte.Graphics.Abstractions.IGraphicDevice device);
+}
+
+// Surface と Native の寿命保持トークンを所有する。GPU 処理は追跡しない。
+public sealed class WindowGraphicsConnection : IDisposable
+{
+    public Lumyte.Windowing.WindowId Window { get; }
+    // 接続が所有する Surface の借用。利用側で直接 Dispose しない。
+    public Lumyte.Graphics.Abstractions.IGraphicsSurface Surface { get; }
+    // 利用側が GPU / 提示の完了と子の解放を確認してから管理スレッドで呼ぶ。
+    // Surface の解放成功後に寿命保持を解除。失敗時は保持して再試行可能。
+    // 冪等。GPU 待機、Present、Window.Close を呼ばない。
+    public void Dispose();
+}
```

Connect の責務は Native の寿命保持と Surface の生成だけである。返す接続が Surface と保持トークンを所有し、呼び出し側が接続を WindowId に対応づける。capabilities の照会、Swapchain の設定、Frame の取得、描画、semaphore、Submit、Present、再構成、子資源の解放は利用側が既存 Graphics 契約に従って管理する。アダプター内で render loop や暗黙の GPU 待機を設けない。接続失敗時は部分生成した Surface を解放してから保持を解除する。同じ表示先を同時に標準経路と直接接続の両方から接続しない責任も呼び出し側にある。

#### 標準外の組み合わせは利用者が直接接続する

OS ハンドルや DOM canvas は WindowState に入れず、`WindowSystem.GetNativeTarget<TTarget>` で明示的に取得する。System は Native、ID の所属と寿命、作成スレッドを検証し、借用 Backend の `IWindowNativeAccess<TTarget>` へ委譲する。未対応の型は NotSupportedException とする。Platform ごとに必要なハンドルを持つ型を定義し、その型の API を各 Platform ADR に記載する。バックエンド独自の型にも同じ取得窓口を使用でき、共通層へ全環境のハンドル union や Graphics 型を持ち込まない。

取得値は借用であり、Native とその OS 接続が有効な間だけ使用できる。取得値のコピーがハンドルの寿命を延長するとは扱わない。直接接続の利用側は RetainNativeTarget で保持トークンを取得してから表示先を取得し、Surface の解放後にトークンを Dispose する。Browser の GPUCanvasContext も Window / Platform が保持し、利用側が canvas を独自に破棄しない。Hosted の接続は拒否するため、利用側が PresentationRoot の Native を明示的に選ぶ。

独立 Vulkan、将来の DirectX、独自 Window ライブラリなど標準経路以外は、利用者が型付きの表示先から Graphics のバックエンド固有 CreateSurface を呼ぶ。例えば Vulkan は生成済み Device の `CreateSurface(callback)` に利用側の callback を渡し、必要な instance extension と present 対応 queue を利用側が用意する。公式にはその組み合わせ専用のアダプターを追加しない。第三者が独自の接続ライブラリを提供することは妨げない。

#### 寸法・配置の受け渡し

Swapchain の Width / Height には観測済みの `WindowState.PixelSize` を使用する。要求した ClientSize、OS 枠の寸法、論理サイズからの再推測を使わない。capabilities に固定寸法がある場合はその観測値と照合し、不整合なら状態更新・再取得を待つ。形式・mode・寸法を黙って補正しない。

ClientSize、Scale、Hosted の Bounds、祖先クリップ、GetHostedOrder は UI Renderer へ渡す。別スレッドへ渡す場合は同じ管理スレッドの更新境界で読み取り、利用側が一つの revision を持つ不変スナップショットにまとめる。描画途中で古い寸法と新しい Scale を混ぜない。GPU 用の投影・viewport・scissor・shader 引数は Renderer が既存の描画 API に変換する。Graphics 共通 API は WindowState や WindowId を引数に要求しない。

サイズ 0、実効非表示、最小化では新規 Frame の取得と提示を停止する。リサイズでは旧 Frame の GPU 使用と取得・提示処理の完了をそれぞれ確認し、View・command・Frame を解放してから対象 Swapchain を Reconfigure する。他の Window の Swapchain を一括で停止・再構成しない。

#### 寿命と終了の責任

通常終了は、入力・テキストセッション停止 → 新規描画停止 → 使用中 Frame の GPU 使用と取得・提示の完了確認 → View・command・Frame・同期資源の解放 → Swapchain・接続の解放 → Window の Close / Dispose とする。直接接続では接続の解放を Surface・保持トークンの解放に置き換える。Surface は外部表示先を所有せず、表示先は Surface の解放まで有効に保つ。

標準アダプターも WindowSystem も、外部の Frame / GPU 使用を追跡・自動解放しない。終了アダプターを組み合わせごとに登録する仕組みは設けず、利用側が上記の終了順を実行する。WindowSystem が管理するのは Graphics に依存しない Native の寿命保持トークンだけである。

親 Close、Hide による一時表示の終了、アプリ非活性化では Window を論理終了して Closed を通知し、新規入力・描画の対象から外す。保持中は Native を非表示にするだけで DestroyNative を延期し、子の Native を物理解放するまで親の Native と共有 OS 接続も維持する。利用側は通知後に残る Frame の処理と接続解放を行う。最後の保持トークンを管理スレッドで Dispose した時点で破棄可能な子から親を解放し、Closed を再通知しない。保持解除時の解放失敗は内部の再解放一覧へ移し、Update / Dispose で再試行する。トークンの Dispose は冪等であり、通知中の保持解除は変更操作として拒否する。

System.Dispose は保持中の Native が一つでもあれば、状態を変える前に InvalidOperationException とする。利用側が接続と保持トークンを解放してから再度呼ぶ。Window.Close の Applied は論理終了を表し、保持中ハンドルの物理解放完了を意味しない。Close 後の新規取得・新規保持は拒否するが、取得済み表示先は保持中の解放処理に使用できる。

Native のユーザー終了要求は Closing.Cancel で延期できる。利用側は通知内で Cancel を true にし、通知後に対象の部分木の Graphics 資源を解放してから Close を要求する。明示 Close / Dispose は終了処理を済ませた利用側が呼ぶ。OS による強制喪失ではハンドルの存続を保証できず、Renderer は Lost / DeviceLost の結果に従って処理する。Device 自体が失われた場合は同じ Device の全表示先を復旧対象とする。

### 更新・通知・エラー・所有権

構成側は Platform の共有イベント取得 → Window / 入力配送境界の反映 → InputSystem.Update → UI / ゲーム更新 → WindowSystem.Update で UI の保留操作を反映 → 描画の順に接続する。最初の Window 反映にも WindowSystem.Update を使用できる。一つの表示フレームで複数回 Update してよいが、共有 OS キューを二重に取得しない。取得中に到着したイベントは次回へ回す。

Update と通知への再入は禁止する。通知中は照会と購読の追加・解除のみ許可し、Closing.Cancel の設定以外の変更操作を `InvalidOperationException` とする。購読者一覧は各通知の開始時点で固定する。購読者の例外は確定状態を巻き戻さず、他の購読者・通知・解放を継続してから AggregateException で返す。Closing 購読者の例外時は安全側に Cancel を true とする。明示 Close、OwnerClosed、BackendLost、Shutdown は取消不能である。

Update は互いに独立した Window の処理を継続する。ExecuteNative の例外は当該要求を Failed にし、全 Window のロールバックは保証しない。DrainEvents 失敗はキューを維持して Update 終了時に報告する。終了処理の例外でも残りの子と他 root の解放を試み、残った Native リソースを診断し、最後に集約する。DestroyNative は失敗後も再試行可能な契約とする。解放失敗でも論理 Window は閉じて配送対象から除外し、Closed を一度だけ通知する。未解放ハンドルだけを内部の再解放一覧で保持し、再試行によって Closed を再通知しない。

Create の初期観測値が不正なら DestroyNative して公開しない。Create 成功通知は次の Update で配送する。Closed は子から親へ一度だけ通知し、閉じた Window の未完了要求もその Update 内で完了させる。Dispose は保持中の Native を変更前に検査し、なければ全 root を生成の逆順、各部分木を子から親の順に解放する。成功後の Dispose は冪等であり、失敗で残ったハンドルは再度の Dispose で解放を試みる。借用 Backend 自体の Dispose は構成側が WindowSystem より後に呼ぶ。

バックエンドのスレッド制約に従って System の作成スレッドを Platform の UI スレッドとする。別スレッドの要求は構成側が同期キューで渡す。不変状態の共有は許可するが、System 自体の並行アクセスを保証しない。

### プラットフォーム別の要求

次は実装の受け入れ条件と制約であり、対応済み一覧ではない。具体的な Window ライブラリ、Linux のプロトコル、Native ABI はバックエンド ADR で決定する。

| 対象 | Native と所有者付き表示 | Hosted | IME と制約 |
| --- | --- | --- | --- |
| Windows | 複数トップレベル、所有関係、非活性表示、モニター DPI の追従を検証 | root 内の共通管理 | OS 標準を既定とし、独自候補は列挙・選択・標準 UI 抑止を実証した実装だけ許可 |
| Linux / X11 | 所有関係と WM による配置・活性化の拒否を検証 | 同上 | 使用する IME 接続方式ごとの能力を報告 |
| Linux / Wayland | 任意のトップレベル絶対位置と前面化を保証しない。所有者付き popup の制約を能力へ反映 | 同上 | compositor / text-input protocol に従い、取得不能な候補は Platform 表示 |
| Browser | 初期対象は構成側が提供した一つの canvas。独立ブラウザー画面作成は未対応と報告し、後続設計へ分離 | 同じ canvas 内で複数領域を管理 | DOM 編集要素を通じた標準変換。候補一覧の取得・抑止を前提にしない |

Browser の初期 canvas の所有権と CreateNative の対応づけはバックエンド ADR で定める。Fullscreen にはユーザー操作などの制約があり、機能対応と当該要求の許可を区別する。Hosted への切り替えはブラウザーの外側や親のクライアント領域外に表示できることを意味しない。

## 検討した代替案

### すべてを OS ウインドウとして生成する

独立描画に適しているが、Browser のメニューや Wayland の位置制御、標準 IME 候補 UI を同じ機能として保証できない。Native と Hosted を区別し、必要な能力を生成時に検証する。

### すべてをメイン画面内の UI とする

一時表示は簡単になるが、複数の独立画面、別モニターのツール、画面ごとの DPI と終了処理を扱えない。表示先の root と Hosted の共通管理を両立する。

### Input または Graphics が Window を所有する

入力デバイスの寿命、GPU リソースの寿命、表示領域の寿命が結合する。Windowing は独立させ、入力配送と提示先は統合アダプターで接続する。

### Native が使えない場合に黙って Hosted へ変更する

親画面外に出せるか、独立提示先があるか、クリップされるかが変わる。実体を明示し、非対応を報告して利用側が選び直す。

### Window と Graphics の組み合わせごとに公式アダプターを提供する

OS、Window ライブラリ、Graphics API の追加で接続実装と検証対象が増える。標準の構成だけを全環境で扱う一つのアダプターに限定し、他の組み合わせは型付き表示先と Graphics の既存入口から利用側が接続する。

## 結果と影響

- 複数画面と一時表示が同じ ID・親子関係・終了契約を利用できる。
- IME 候補やメニューを表示しても編集対象の入力フォーカスを維持できる。
- 要求と観測、Native と Hosted を分けるため、OS の制約を成功したように見せない。
- Hosted の内容描画、文字編集、UI 入力スコープは別の責務として実装が必要となる。
- 初期 Window 共通層は GPU 提示・入力配送・IME を単独では完成させない。GPU 提示は既存 Graphics 契約を使用し、標準の接続アダプターと実環境検証が必要となる。
- 公式の接続アダプターは一つとなり、全環境で同じ入口を使える。標準外の Graphics / Window の接続と寿命管理は利用側の責務となる。
- 状態スナップショットと保留要求の保存にメモリを使う。閉じた Window を System 内に永久保存せず、必要な履歴は購読側が保持する。

## 検証方針

共通層の実装時は偽の Backend を使い、以下を単体テストする。

- 複数 root の独立した状態、System を跨ぐ ID の拒否、閉じた ID / ハンドル再利用の区別。
- 作成失敗・不正初期状態・非対応機能で Native リソースと公開一覧に残骸がない。
- Native / Hosted の親制約、役割・層・有限座標の検証、生成時は必ず非表示。
- 要求と観測値の分離、各要求の一回限りの完了、拒否・期限切れ・置換・終了時の失敗通知。
- Hosted の親より前の描画、層を跨ぐ Raise の制限、部分木の前面移動、クリップ後のヒットテスト。
- キャレット付近と四辺での popup 反転・クランプ、負のアンカー、領域より大きな popup、非整数スケール。
- 親の Hide / Minimize / Close と BackendLost、メニューの終了、通常の子の再表示、独立 Tool の存続。
- PreserveFocus の表示・Raise・ポインター操作、対象終了時の祖先復帰、root 切り替えとアプリ非活性化。
- 連続するフォーカス・配置変更と入力を同じ取得バッチに入れ、発生時の配送先を保持する統合テスト。
- 通知例外・再入・閉じる取消、強制終了、子から親の一回限りの通知、解放失敗後の再試行。
- 型付き表示先の借用、未対応型・Hosted・別 System・終了済み ID の拒否、標準外の組み合わせで Connect が自動 fallback しないこと。
- Native の保持中に親終了・非活性化が来ても物理ハンドルを先に破棄しないこと、子から親の遅延解放、接続失敗時の保持解除、保持中 Dispose の変更前拒否。

実環境の受け入れでは、二つの Native 画面を異なる DPI のモニターへ移動しながらメニューを開く。表示位置、PixelSize、フォーカス、親終了時の子の解放を確認する。Wayland と Browser では非対応操作が拒否として観測され、Hosted を明示選択した場合に表示できることを確認する。

文字入力の統合後は、日本語変換中に候補をクリックしても編集領域を失わないこと、古い候補 revision の選択を拒否すること、OS 標準と独自候補の二重表示がないこと、キャレット移動・DPI 変更・アプリ切替・親終了で候補とセッションが残らないことを検証する。Graphics 提示の統合後は、一画面の最小化・強制喪失・再構成中も他画面を描画でき、通常終了で GPU 利用後に Native を解放することを検証する。

標準アダプターは Windows、X11、Wayland、Browser で同じ Connect 入口から Surface を生成できることを検証する。独立した二つの Native の表示・片側のリサイズ・終了延期と切り離し、非整数 DPI、Hosted メニューの同一 Surface への描画を確認する。直接接続の利用例を一つ用意し、標準アダプターを使用せず型付き表示先から接続できることと、利用側が既存 Graphics の同期・終了契約を満たすことを検証する。

この設計 PR 自体では Markdown lint、相対リンクと比較元 revision、API の名前と本文の契約の整合を確認する。実装・性能・実機の検証結果とは区別する。

## 別途決定する事項

- 各 Platform の Window ライブラリ、イベント取得器、Linux の実装対象、Browser の canvas 所有権と独立画面生成。
- Native の非同期生成、要求完了期限、バックエンド異常時の再接続と Native ABI。
- Window 対象付き入力 envelope、取得イベント間の共通順序、UI 入力スコープ、ポインターキャプチャ・ロック。
- テキスト入力セッション、変換・確定通知、候補 snapshot / revision / 選択、標準 UI 抑止とセッション終了時の変換方針。
- Platform ごとの型付き表示先の API、標準アダプターの対象環境別ビルドと標準 Window バックエンドの選定。Surface・Swapchain・同期は GRAPHICS-0013 に従う。
- モーダル画面、ファイルダイアログ、ドラッグアンドドロップ、アクセシビリティ、永続配置、性能目標。

## 参考資料

- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [ADR-INPUT-0001: デバイス別の入力記録と参照・通知・ポーリング](../input/INPUT-0001-input-system.md)
- [ADR-GRAPHICS-0001: Graphics Device](../graphics/GRAPHICS-0001-graphics-device.md)
- [ADR-GRAPHICS-0013: Surface・Swapchain・Present の明示的な管理](../graphics/GRAPHICS-0013-surface-swapchain-presentation.md)
- [ADR-GRAPHICS-0014: 利用側によるリソース管理と検証範囲](../graphics/GRAPHICS-0014-caller-managed-resource-validation.md)
