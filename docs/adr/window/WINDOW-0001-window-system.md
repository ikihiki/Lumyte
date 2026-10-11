# ADR-WINDOW-0001: Platform の上に置く任意の複数ウインドウ管理

- 状態: 提案
- 日付: 2026-10-10
- 更新日: 2026-10-11

## 背景

ゲームの複数画面、ツール、ポップアップ、コンテキストメニュー、IME 候補一覧は、親子関係、表示順、フォーカス、配置、終了の管理を必要とする。一方、単一ウインドウのアプリケーションには、その管理ライブラリを必須にする必要がない。

[Platform の共通契約](../platform/PLATFORM-0001-platform-and-window-contracts.md)が Native の IWindow を生成し、各バックエンドが IWindow を直接実装する。Windowing は生成された IWindow を利用する上位ライブラリとして、複数ウインドウと Hosted の一時表示を管理する。本 PR は設計案だけを追加し、以下の API は未実装である。

## 決定

### 立ち位置と依存方向

`src/Platform/Lumyte.Windowing/`、名前空間・NuGet を `Lumyte.Windowing` とする。Windowing は Platform.Abstractions を参照し、Platform / 各バックエンドは Windowing を参照しない。Windowing のための IWindowBackend や Native 命令転送 interface は設けない。

Native の生成・操作・観測・OS イベント取得・ハンドル・寿命保持は IPlatform / IWindow が担当する。Windowing の WindowSystem は IPlatform を借用し、必要な IWindow を作成または明示登録する。IWindow を共通の Native wrapper に置き換えない。Hosted の領域は Windowing の管理ノードであり、IWindow を実装しない。

| 構成要素 | 責務 |
| --- | --- |
| Platform.Abstractions / Platform バックエンド | IPlatform による生成、IWindow の直接実装、入力取得と Native の操作・寿命 |
| Windowing | 複数 IWindow の管理、明示所有、管理ノードの親子関係、Hosted の配置・クリップ・描画順、一時表示の寿命 |
| Framework 層の統合システム | Platform と Graphics の接続、Surface・Swapchain・同期・終了の管理。単一 Window では Windowing 不要 |
| UI / Engine / 利用側 | 内容描画、文字編集、入力スコープ、GPU 同期・提示、外部リソースの終了 |

単一 Native は IPlatform.CreateWindow → IWindow 操作 → Framework 統合または利用側の直接 Graphics 接続で使える。WindowSystem の生成・参照・DI 登録を必要としない。単一 Native の上に Hosted メニューを表示する場合は、必要に応じて Windowing を追加する。

### Native と Hosted の管理単位

WindowSystem は二つの種類の管理ノードを持つ。

- Native ノード: 実体は Platform が返した IWindow。独立した Graphics 提示先を持つ。所有者付き Native も独立した提示先であり、自身が PresentationRoot となる。
- Hosted ノード: 必ず親を持ち、最も近い Native 祖先の提示先に描画する。親座標の Bounds、表示意図、Activation を Windowing が保持する。

役割は Normal、Tool、Popup、ContextMenu、ImeCandidates とする。役割は Windowing の UI 管理方針であり、Platform の Native WindowKind と区別する。Native の Popup / ContextMenu / ImeCandidates は Platform の Popup / PreserveFocus と対応させる。Hosted の一時表示も PreserveFocus とする。

Native 子は Native 親のみ許可し、IWindow.State.Owner が親 IWindow.Id と一致することを検証する。Hosted 親の下に Native を作る際の所有者推測は行わない。Attach は Native の Owner がある場合に先にその Owner のノードを登録する必要がある。独立して存続する Tool は Owner / 管理 Parent を付けずに作る。

Native の非対応機能を黙って Hosted に切り替えない。利用側が別の生成要求で Hosted を選ぶ。Hosted は UI ウィジェットではなく表示境界であり、メニュー項目、候補内容、装飾、アクセシビリティ、アニメーションは UI 側の責務とする。

### 公開 API 案

比較元は main の `cc766f2`。Windowing は存在しないため以下はすべて追加する主要宣言である。Native の型は Platform.Abstractions の契約を使用する。

```diff
+using Lumyte.Platform.Abstractions;
+
+namespace Lumyte.Windowing;
+
+// Platform の WindowId と区別する。Hosted にも割り当て、終了後は再利用しない。
+public readonly record struct WindowNodeId(Guid SystemId, ulong Value);
+public readonly record struct WindowNodeRequestId(ulong Value);
+public enum WindowRole { Normal, Tool, Popup, ContextMenu, ImeCandidates }
+public enum WindowNodeKind { Native, Hosted }
+public enum WindowOwnership { Borrowed, Owned }
+
+public sealed record HostedWindowDescriptor
+{
+    public required WindowNodeId Parent { get; init; }
+    // 親のクライアント座標。有限値、Width / Height は正。
+    public required WindowRect Bounds { get; init; }
+    public WindowRole Role { get; init; } = WindowRole.Normal;
+    public WindowActivation Activation { get; init; } = WindowActivation.Activate;
+}
+
+// System が生成する不変値。Native の実体と観測は IWindow.State を正とする。
+public sealed record WindowNodeState
+{
+    public WindowNodeId Id { get; }
+    public WindowNodeId? Parent { get; }
+    public WindowNodeId PresentationRoot { get; }
+    public WindowNodeKind Kind { get; }
+    public WindowRole Role { get; }
+    public WindowOwnership Ownership { get; }
+    // Native だけ値を持つ。Hosted の ID を Platform の ID として扱わない。
+    public WindowId? NativeWindow { get; }
+    public WindowRect Bounds { get; }
+    public System.Numerics.Vector2 ClientSize { get; }
+    public WindowPixelSize PixelSize { get; }
+    public System.Numerics.Vector2 Scale { get; }
+    public bool IsVisible { get; }
+    public bool IsEffectivelyVisible { get; }
+    public WindowLifetime Lifetime { get; }
+}
+
+public sealed class WindowSystem : IDisposable
+{
+    // Platform を借用。OS 選択や Native Backend の実装は行わない。
+    public WindowSystem(IPlatform platform);
+    public IReadOnlyList<WindowNodeState> Windows { get; }
+    public WindowNodeId? KeyboardTarget { get; }
+    // IPlatform.CreateWindow の結果を System 所有として登録する。
+    // descriptor.Owner と Native 親の整合を事前検証する。
+    public WindowNodeId CreateNativeWindow(
+        WindowDescriptor descriptor, WindowRole role, WindowNodeId? parent = null);
+    // 同じ Platform の IWindow を明示登録。Owned は登録成功後に所有権移転。
+    // Borrowed は Close / Dispose せず、利用側が所有し続ける。
+    public WindowNodeId Attach(IWindow window, WindowRole role, WindowOwnership ownership);
+    public WindowNodeId CreateHostedWindow(HostedWindowDescriptor descriptor);
+    public WindowNodeState GetState(WindowNodeId node);
+    // Native にのみ許可。Hosted なら PresentationRoot を明示して取り直す。
+    public IWindow GetNativeWindow(WindowNodeId node);
+    public IReadOnlyList<WindowNodeId> GetHostedOrder(WindowNodeId presentationRoot);
+    public WindowNodeId? HitTest(WindowNodeId presentationRoot, System.Numerics.Vector2 point);
+    public WindowNodeRequestId Show(WindowNodeId node);
+    public WindowNodeRequestId Hide(WindowNodeId node);
+    public WindowNodeRequestId Activate(WindowNodeId node);
+    public WindowNodeRequestId BringToFront(WindowNodeId node);
+    public WindowNodeRequestId Place(WindowNodeId node, WindowPlacement placement);
+    public WindowNodeRequestId SetHostedBounds(WindowNodeId node, WindowRect bounds);
+    // Hosted / Owned Native を子から親へ終了する。Borrowed Native は拒否。
+    public WindowNodeRequestId Close(WindowNodeId node);
+    // 借用 Native の登録解除。管理下の Owned / Hosted 子は先に終了する。
+    // 解除対象の IWindow は Close / Dispose しない。
+    public WindowNodeRequestId Detach(WindowNodeId node);
+    // Platform 通知と保留管理操作を反映する。PumpEvents を呼ばない。
+    public void Update();
+    public event Action<WindowNodeState>? Changed;
+    public event Action<WindowNodeId>? Removed;
+    public event Action<WindowNodeRequestResult>? RequestCompleted;
+    // 借用 Native のユーザー終了要求などを利用側の終了方針へ通知する。
+    public event Action<WindowNodeId>? CloseRequested;
+    // 所有 Native と Hosted を解放、借用 Native は購読解除するだけ。
+    // 保持中の Native は解放せず失敗を報告、解放済みは再試行で重複処理しない。
+    public void Dispose();
+}
+public sealed record WindowNodeRequestResult(
+    WindowNodeId Node, WindowNodeRequestId Request, WindowRequestStatus Status, string? Error);
```

SystemId は System ごとに異なる識別子、番号 0 は無効とする。Platform.WindowId、管理ノード ID、各 RequestId の採番範囲を混同しない。Native の要求結果は `(IWindow.Id, WindowRequestId)` で管理要求へ対応づける。同じ IWindow の重複 Attach、別 Platform、別 System の親、不正役割・数値は ArgumentException 系、終了済みノードは KeyNotFoundException、破棄後は ObjectDisposedException とする。

Attach は対象が Open の場合だけ許可する。所有権は登録成功後に移転し、失敗時は呼び出し側に残る。CreateNativeWindow の登録失敗では今回生成した IWindow を解放する。共有による二重管理を避けるため、一つの IWindow を同時に複数 WindowSystem で管理しない責任は構成側にある。

### 描画順・座標・フォーカス

Native ノードの Bounds は `(0, 0, ClientSize.X, ClientSize.Y)`、PixelSize / Scale / 可視性は IWindow.State に従う。Hosted は親クライアント座標であり、祖先を合成して root へ配置し、祖先のクリップを交差する。整数ピクセル寸法は実際の Native PixelSize を基準とし、Native の OS 枠や取得不能な画面絶対位置を推測しない。

Hosted の順序は root ごとに持つ。親より子を前面に置き、Normal / Tool の層、Popup / ContextMenu の層、ImeCandidates の層を順に描画する。子は親以上の層を必須とする。同層では作成順、BringToFront は対象部分木を前面へ移動する。異なる Native の Z 順を Hosted の順序へ混ぜない。Native の Raise は IWindow.BringToFront の要求である。

Hosted の Place は親座標の Anchor から希望側、反対側、root の可視領域内へのクランプの順に配置する。サイズを勝手に縮めず大きい場合はクリップする。親の配置・DPI 変更で再計算する。Native の Place は IWindow.Place を呼び、Platform の機能と観測結果に従う。

HitTest は root のクライアント座標を受け取り、実効非表示と祖先クリップ外を除外して最前面を返す。Hosted がなければ root、root 外または root 非表示なら null とする。WindowRect 全体を入力領域とし、内容ごとの透過判定は UI 側へ委ねる。

KeyboardTarget は OS の active Native 内の論理入力対象である。Native の活性化は IWindow の観測成功を待ち、Hosted の Activate は実効可視性と Activation を検証して変更する。PreserveFocus の表示・Raise・ポインター操作では変更しない。対象終了時は最も近い有効な祖先へ戻し、アプリ非活性化では null とする。root 切り替えでは root ごとの最後の有効な対象を復元する。

メニューの矢印・Enter・Escape は UI の一時入力スコープが現在の対象から消費する。候補ポインター操作も元の編集領域へ選択要求を渡す。WindowSystem はキーを文字や候補確定に変換しない。

### 所有権・終了・単一 Window との共存

Owned Native は System が IWindow.Close / Dispose する。Borrowed Native は System が購読と管理関係だけを所有し、利用側が Native の終了・解放を担当する。Attach(Borrowed) によって単一 Window の所有者が変わることはない。

通常は System 経由で管理対象を操作する。GetNativeWindow は Graphics 接続や Backend 固有操作のための借用であり、Owned の Native を利用側が勝手に Dispose しない。利用側が IWindow を直接操作した場合も、System は StateChanged を受けて観測値に追従する。

Close は対象部分木を子から親の順に終了する。Borrowed Native が部分木にあれば Close を事前に拒否し、利用側が先に Detach して所有者側で終了する。Owned Native の親は、子 Native の物理 Closed を確認してから Close する。Hosted は即座に終了できる。Native が保持中なら Closing のまま待ち、Graphics 資源を Framework または利用側が解放した後に Platform.PumpEvents で Closed へ進む。

Close の管理要求は対象が閉じて管理一覧から除去されたとき完了する。Detach は Owned / Hosted 子を先に終了し、借用子は借用のまま購読解除してから対象を登録解除する。外部所有の Native の終了や登録解除と、管理ノードの Removed 通知を混同しない。

Native のユーザー終了要求では管理下の子の終了が必要な場合に Closing.Cancel を設定して延期し、管理スレッドの次の Update で処理する。Owned なら子の完了後に明示 Close、Borrowed なら CloseRequested で利用側へ知らせ、利用側が Detach と Native.Close を進める。購読者の通知中に IWindow の変更操作を呼ばない。

Hide は対象の表示意図を false にし、通常の Hosted 子の表示意図は維持して実効非表示にする。一時表示の Owned / Hosted 子は終了し、親を再表示しても復活しない。借用の一時表示 Native は自動 Close せず非表示と CloseRequested の通知に留める。最小化・アプリ非活性化も同じ方針とする。OS の強制喪失では利用側が他 Native の描画継続と Lost / DeviceLost を処理する。

System.Dispose は Framework または利用側が Owned Native の Graphics 資源を先に解放した状態で呼ぶ。Native の保持や解放失敗が残る場合は独立した他ノードの解放を続けて例外を集約し、後の Dispose で残りだけ再試行する。借用 Native は Dispose しない。Platform 自体は全 Native と Source の解放後に利用側が Dispose する。

### Graphics との接続

Graphics 接続は [Framework 層の統合責務](../platform/PLATFORM-0001-platform-and-window-contracts.md#framework-層による-graphics-統合)に従う。Windowing は Native の IWindow と描画に必要な管理状態を提供し、Framework が Platform と Graphics を接続する。

Native の GetNativeWindow の結果を Framework の統合システムへ渡す。Hosted は root の IWindow / Surface に描画し、独立した Surface を作らない。WindowSystem は Surface、Frame、GPU 使用を追跡せず、Framework がその対応と寿命・同期を管理する。

Framework と Renderer は観測 PixelSize から Swapchain を明示設定し、ClientSize / Scale / Hosted 配置・クリップ・GetHostedOrder を同じ更新境界の不変スナップショットへまとめる。別スレッドへの受け渡しは利用側が同期し、古い寸法と新しい倍率を混ぜない。GPU 用の投影・viewport・scissor・shader 引数は Renderer が既存 Graphics API へ変換する。

リサイズでは当該 root の GPU 使用と提示処理の完了、Frame・子資源の解放を確認して再構成し、他 root を一括停止しない。通常終了は新規描画停止 → GPU / 提示完了 → 子資源・Swapchain・接続解放 → Native 保持解除 → Owned IWindow の終了・解放とする。

### Input・文字入力・IME

OS の取得は IPlatform.PumpEvents 一回で行い、IWindow の状態と Input Source のキューを同じ取得順で更新する。WindowSystem は入力取得器にならず、既存 InputSystem は独立して利用できる。

最終入力配送先は発生時点の Native、Hosted 構造・座標・フォーカスから決める。更新後の最終状態だけで過去入力を再ヒットテストしない。共通順序を持つ envelope と InputRecord の対応は後続 Input / Platform ADR で具体化する。Platform の WindowId、管理ノード ID、各 System の Sequence を大小比較して統合しない。

単一 Window の文字入力も Platform のテキスト入力セッションで扱い、Windowing を必須にしない。Windowing 使用時の Hosted 編集領域は Platform の対象 Native と、root 座標へ変換したキャレット矩形を渡す。スクロール・親配置・DPI 変更で再計算する。入力フォーカス喪失・終了時にセッションを停止し、古いセッションの遅着通知を適用しない。

IME 候補表示はセッションごとに次のどちらか一方とする。

- Platform 表示: OS / Browser が標準候補 UI を表示する。Windowing はそれを所有 Window として登録しない。
- Application 表示: 候補列挙・選択・標準 UI 抑止が成功した場合だけ、UI が ImeCandidates / PreserveFocus の Native または Hosted を作る。

独自候補 UI に Windowing を使うことは任意であり、利用側が IWindow から直接生成・制御してもよい。標準 UI 抑止が失敗した場合は候補ノードを表示しない。候補選択はセッション ID と snapshot revision を伴わせ、古い index を適用しない。変換終了・キャンセル・対象終了で候補を終了する。確定文字列は正式なテキスト通知からだけ受け取り、Window の通知・診断へ候補文字列を混ぜない。

### 更新・通知・エラー

通常のフレームは Platform.PumpEvents → WindowSystem.Update で観測反映 → InputSystem.Update → UI / ゲーム → WindowSystem.Update で管理操作反映 → 描画とする。System を使わない場合はその Update を省略する。Native の保留要求は次の PumpEvents で反映してもよく、即時反映が必要なら同じ管理スレッドで追加の PumpEvents を明示する。

Platform の通知中は System がイベントをキューへ保存するだけとする。ユーザー Close の取消だけはその通知中に設定できる。System.Update は IWindow の操作を直接呼び、Platform を代行して OS を取得しない。通知中・処理中の再入と変更操作は拒否し、不変値の照会は許可する。

System は Hosted の状態と管理要求を確定し、Native の要求は IWindow の完了を待つ。要求は一度だけ Applied / Denied / Unsupported / Failed / Superseded として結果を通知する。通知例外で確定状態を巻き戻さず、他の購読者・独立ノードを処理してから AggregateException とする。Native 操作の失敗で他 root を巻き戻さない。

## 検討した代替案

- Native の共通実装と IWindowBackend を Windowing に置く: 単一 Window でも上位層を必須とするため採用しない。
- Hosted に IWindow を実装させる: OS 表示先と同じ機能・寿命を保証できないため管理ノードとして分ける。
- Attach で所有権を常に移転する: 単一 Window のアプリが後から管理機能を追加しにくいため借用も許可する。
- すべてを Native とする: Browser / Wayland の制約があるため Native と Hosted を明示する。
- すべてをメイン画面内の UI とする: 独立画面・複数モニター・個別 DPI を扱えない。

## 結果と影響

Platform と Native の契約は単独で利用でき、複数ウインドウ・Hosted 管理が必要なアプリだけ Windowing を追加できる。Native の操作・通知は Backend が担当し、Windowing は UI の管理方針に集中する。二種類の ID と明示所有権が必要となり、利用側は借用と所有の終了責任を区別する必要がある。

## 検証方針

- 偽の IPlatform と直接実装した IWindow を使い、Windowing が Backend 転送 interface を要求しないことを確認する。
- 単一 Window サンプルが Windowing を参照せず、同じ IWindow を任意に Attach / Detach できる。
- Owned / Borrowed の登録失敗・終了・Dispose、同一 Window の重複、ID の混同と親の不整合を拒否する。
- Native の状態追従、要求完了の対応づけ、PumpEvents の二重取得防止、Close 延期と子から親の終了を検証する。
- Hosted の層・部分木・クリップ・ヒットテスト、アンカーの反転・クランプ、DPI を検証する。
- PreserveFocus、root 切り替え、祖先復帰、外側クリック・Escape の UI 方針、非活性化と一時表示の終了を検証する。
- 同一取得バッチの配置・フォーカス変更と入力で発生時点の配送先を保持する。
- 通知例外・再入・解放失敗の再試行、借用 Native を勝手に閉じないことを検証する。
- 実環境で二つの Native の DPI 変更、片方の最小化・終了、Hosted メニュー、IME の二重候補表示防止と候補 revision を確認する。

設計 PR 自体は Markdown lint、相対リンク、API diff と責務・所有権の整合を確認する。実装・性能・実機対応は未検証。

## 別途決定する事項

- Window 対象付き入力の envelope、キャプチャ・ポインターロック、UI 入力スコープ。
- Platform の文字入力・IME セッション、候補 snapshot、選択、終了時の確定 / キャンセル方針。
- モーダル画面、永続配置、アクセシビリティ、性能目標。
- Framework 統合システムの具体 API と、Windowing を使用する場合の接続・更新・終了順。

## 参考資料

- [Platform の共通生成と IWindow の直接実装](../platform/PLATFORM-0001-platform-and-window-contracts.md)
- [ADR-0001: ADR の書き方と運用](../0001-adr-writing-policy.md)
- [ADR-0002: リポジトリのフォルダ構成](../0002-repository-layout.md)
- [Input の設計](../input/INPUT-0001-input-system.md)
- [Surface・Swapchain・Present](../graphics/GRAPHICS-0013-surface-swapchain-presentation.md)
