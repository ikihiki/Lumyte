# ADR-UI-0036: 通常の内容合成と所有者駆動のダイアログ・通知

- 状態: 採用
- 日付: 2026-11-17
- 基準コミット: `0f89bac4cef1d2ecc3adeea19c6036fbcf6ea27d`

## 背景

アプリの作者は、確認画面と一時通知を既存の入力・編集・表示寿命へ接続したい。基準コードには Popup の宣言と表示管理がある一方、[ダイアログの提案](UI-0022-declarative-hosted-dialogs.md#決定)に対応する実装と通知の待ち行列はない。通常の内容合成と、表示先・入力遮蔽・期限の管理を別の責務にする必要がある。

## 決定

DialogContent と Toast は通常の内容、Dialog は独立した表示面へ接続する宣言、ToastController は一つの ToastHost に借用される有界待ち行列とする。入力と表示の管理は既存の IUiWindowManager、UiPopupManager、UiSurface と Application の所有者ループを再利用し、別の OS Window 管理系や命令的な Open サービスを追加しない。

### 利用者の変更前・変更後

変更前は、見出し・本文・操作列を Stack へ並べ、通知の待機順序と期限もアプリで管理する。文字組みとフォントは変更後も呼び出し側が明示する。

```csharp
using static Lumyte.UI.Compose;

var content = Stack()[heading, body, actions];
```

変更後は、同じ内容を名前付き領域へ指定し、表示意図を Dialog へ渡す。次の例の heading、body、actions は呼び出し側が作る Widget、open は所有者上で保持する `Signal<bool>` であり、既存の構築と解放の外側のコードを省略する。

```csharp
using Lumyte.UI.Controls;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.Controls.Windowing.DialogCompose;

var portal = Dialog("details", "詳細", isOpen: open,
    onCloseRequested: _ => open.Value = false)[
    DialogContent()[
        DialogContent.Header()[heading],
        DialogContent.Content()[body],
        DialogContent.Actions()[actions]
    ]
];
```

通知は所有者上で一回作った制御器を借用する。次の例は queue とその借用時計の生成・解放、および ToastHost を置く親を省略する。閉じるボタンは要求を通知するだけで、アプリが対象 ID を Dismiss する。

```csharp
ToastId id = default;
id = queue.Enqueue(
    () => Toast(onDismissRequested: () => queue.Dismiss(id))[
        Toast.Content()[CreateMessage()]
    ],
    TimeSpan.FromSeconds(3));
var overlay = ToastHost(queue);
```

### 配置と依存方向

表示宣言と coordinator は Lumyte.UI.Windowing に置く。既存 Application が同 assembly を参照しているため、通常の Application で追加登録や手動の Advance を要求せず、Core UI から Controls.Windowing への逆依存も生じない。Controls.Windowing の DialogCompose は Windowing.Compose.Definitions.Dialog を返す薄い静的 factory とし、assembly を跨いだ partial Compose を共有しない。

DialogContent、Toast、ToastHost、ToastController と基本八種類の UiRecipes は Controls に置く。Submenu は Controls.Windowing.MenuCompose で既存 MenuItem とその CreateSubmenu へ展開する九種類目の recipe であり、別の Widget 型やメニュー状態を追加しない。

基準コードの根拠は、固定コミット `0f89bac4cef1d2ecc3adeea19c6036fbcf6ea27d` の `src/UI/Lumyte.UI.Windowing/UiPopupPortalCoordinator.cs`、`UiPopupManager.cs`、`IUiWindowManager.cs` と `src/UI/Lumyte.UI.Application/UiApplicationViewSlot.cs` である。[部品一覧](UI-0013-component-catalog-and-delivery-plan.md#決定)の未実装候補を、ここで定める有限範囲の契約として具体化する。

### 通常の内容とラベルの関係

DialogContent と Toast の Header、Content、Actions は、それぞれ省略可能な一つの通常 root を所有する。複数の操作は Actions に置く通常の Stack 等で合成する。DialogContent.Close と Toast.Dismiss は省略可能な一つの表示部品であり、前者を指定すると host 閉鎖要求を送る通常 Button、後者は Toast の通常の閉じる Button の表示内容になる。

DialogContent の通常領域は親の残り制約で各子を一回測定する。Button を含む合成も既存の一方向 layout、with 修飾、slot の一回初期化と所有規則を使う。DialogContent は表示先や modal を開始せず、Close は接続先 UiSurface の閉鎖要求を通して現在の Dialog.OnCloseRequested へ届く。

Toast の閉じる Button は 24×24 の通常入力・focus・Automation target であり、Toast 自身は別の focus owner を持たない。DismissName は明示名、Dismiss は任意の表示内容である。Header と本文は借用宣言を変更しない入力無効の通常 wrapper で描画し、その子孫も focus・IME を開始しない。本文と空の ToastHost は hit を透過し、Actions と閉じる Button の領域だけが入力を受ける。文字、icon、色、font provider はアプリが供給する。

八種類の recipe は IconButton、Card、FormRow、ValidationMessage、Toolbar、StatusBar、EmptyState、PropertyField とする。入力状態を新設せず、通常の Button、Stack と既存 Border・Padding 操作へ展開する。Card の簡便形は透明・幅ゼロの枠と余白 8、明示形は色・線幅・余白を引数で受ける。

FormRow は通常の二つの直接子について UiLabelAssociation を保持する。位置 0 が label、位置 1 が editor、任意の AccessibleName はアプリの明示値であり、見た目から文字を抽出しない。構築 recipe に含まれる受動 metadata なので再構築先にも保存され、共有 editor 宣言を書き換えない。現在の Automation snapshot や OS bridge にこの関係が自動で追加されるとは扱わず、編集部品自身の AutomationName も必要ならアプリが明示する。

PropertyField はアプリが型に応じて選んだ editor を配置し、reflection で選択しない。ValidationMessage(null) は空、EmptyState の action と PropertyField の validation は省略可能とする。Toolbar は通常の Tab 移動を使い、矢印による専用の選択方式を追加しない。

### ダイアログの表示と入力

現在の表示範囲は Hosted である。Automatic は固定理由 native-unavailable を添えて Hosted を選び、NativeRequired は内容の recipe 評価、surface 作成、計測の前に Failed を一回通知する。既存の Native Popup 能力や PreserveFocus を、活性化可能な Native modal の能力と同一視しない。Native adapter の保証を持たない環境で、NativeRequired を黙って Hosted に置き換えない。

IsOpen の正本はモデルであり、Escape、外側 press、内容の閉じる Button は最新 OnCloseRequested へ一系列一回の要求を送る。モデルが閉じなければ表示は続く。CloseOnEscape=false の modal Escape、CloseOnOutsidePress=false の modal 背景 press も背後へ配送しない。modeless の外側要求は通常の配送を遮蔽せず、modeless Escape も同じ要求経路を使う。

表示は予約、入力終了待ち、非公開 binding、Show、提示確認、Open の順で進む。同じ key と mount identity は session、内容の Element、入力・文書状態を維持し、新しい宣言を reconcile する。初期寸法は正の有限な 320×200 を既定とし、InitialFocus は内容内の一意 key、未指定または不適格なら最初の候補を使う。重複は提示前に拒否する。

一つの manager は入れ子を含め最大 16 session、一つの所有表示面は最大 16 Dialog 宣言を受け付ける。失敗した true は清掃後に停止状態で保持し、モデルの false、公表済み宣言の除去、または表示方式の変更を経るまで再試行しない。Closing 中の true は旧 session を復活させず、清掃後の新しい session とする。所有 Window が非表示または利用不能な間は true の意図を保持して新規予約を待ち、未予約の失敗通知は送らない。既存 session は一回だけ終了通知し、再表示後は旧清掃が完了してから最新の宣言で一つだけ新規 session を開始する。

入力遮蔽は Menu mode とは独立した論理所有関係で判定する。Dialog、同じ所有者の title bar・resize grip、子 Popup と IME candidate を関係付け、物理 keyboard scope の GUID だけで別の Hosted Window まで遮蔽しない。pointer、wheel、key、Automation の probe と通常 lease 配送は同じ現在の許可範囲を検査する。

ここでいう backdrop は所有範囲の外側入力を消費する透明な障壁であり、暗黙の色・調光・装飾を追加しない。Modal 内の未処理 key と pointer・wheel も消費し、通常の Tab と Shift+Tab はその surface の候補を巡回する。背景の進行中入力は取消して終端まで消費し、owner の menu と candidate を終了・drain し、編集の suspension が完了してから新しい内容を公開する。Application は root だけでなく各 Dialog の実際の論理所有者へ、その編集接続の非同期 guard を一回登録する。入れ子 Dialog の内容生成・binding・Show・OnOpened と、その editor から開く Menu の提示は、preedit と candidate の退役を待つ。登録は Dialog の清掃寿命に属し、編集接続の破棄前に解除する。

modeless も初回の編集・candidate 退役を待つが、提示後は所有者の編集 suspension を解放し、表示中も所有者の通常入力と IME 接続を再開できる。modal は清掃まで suspension を保持し、IsModal の変更では旧表示を退役して新しい方針で準備し直す。入れ子の外側 press は適用される論理所有関係の最内 modal だけへ要求し、その方針が拒否しても祖先へ閉鎖要求を送らない。

focus の復帰先は開始前の接続 ID、実表示面、keyboard target、明示 focus 意図の世代で判断する。清掃中の新しい所有者選択や focus 要求を古い bookmark で上書きしない。入れ子は生存する許可範囲へ戻し、古い接続が失効した場合は新しい意図がないときだけ最初の候補を使う。

### 通知の有界待ち行列と時計

ToastController の capacity は表示中の一件を含め 1〜1024、既定 16 とする。一件だけを FIFO で表示し、満杯の Enqueue は factory を呼ばずに拒否する。Snapshot は ID と現在の表示有無だけを持ち、内容や delegate を外へ渡さない。

Duration はゼロを sticky、それ以外は正で一日以下とし、負値や過大値を拒否する。表示時間は待機開始や非公開の内容準備ではなく、先頭の内容が表示可能になった最初の所有者更新から数える。借用した IMonotonicClock を所有者上で読み、一回の Advance は最大一件だけ期限切れにする。独自 Task.Run、OS timer、scheduler thread は作らない。

通常の Application では host の mount に対応して既存の frame 更新へ登録し、sticky または空のときは時計用 frame を要求しない。retained Tab など実効非表示の host は expiry mutation を止めるが期限そのものは単調時間で進み、一度表示を開始した内容は再表示後の最初の更新で期限切れを処理する。まだ一度も表示していない内容は、初めて表示できる更新から期限を開始する。待機中の次の通知は、そのときから新しい表示時間を得る。

内容 factory は先頭になるとき一度だけ評価する。新しい、親も既存 mount も持たない root を要求し、静的な子と準備後の動的な子を同じ所有 tree で検証する。factory または検証の失敗では旧表示と queue 順序を保持し、失敗した内容を再評価しない。失敗した待機 ID は Dismiss して除去できる。

制御器は生成した所有者 thread に所属し、一つの host だけが借用する。別表示面への共有や factory 内の再入命令は、内容の callback より先に予約で拒否する。任意 thread の命令は明示 IUiDispatcher を渡す EnqueueAsync または DismissAsync を使う。

Dismiss は不明 ID に対して何もせず、expiry と通常の取り除きは受理した除去の後に Enqueue の onClosed を一回呼ぶ。Clear は有界 snapshot を除去してから各 callback を呼び、例外があっても残る callback を処理する。公開後の input 取消や unmount 通知が失敗しても、既に受理した除去の callback は失わない。host の unmount はその queue 内容を破棄して借用を終了し、制御器 Dispose は通知を送らず admission と frame 登録を止める。既に借用している同じ mount の ToastHost は、その後の Signal 更新でも空かつ入力透過のまま reconcile できる。これは新しい host の借用を許可せず、公開命令は Dispose 後に拒否する。同じ Element が別の制御器を持つ新宣言へ移る場合は通常の借用規則を使い、停止済み制御器は host・内容・時計用 frame を保持しない。

### 清掃と診断の境界

Dialog は子 Dialog と Popup、入力、text・Automation・視覚参加者、binding の GPU 退役、surface、Window の Close・Release、編集 suspension、modal token の順に清掃する。取消 callback が throw しても他の清掃を開始し、完了 task の失敗を観測する。安全な退役を終えた失敗は cleanup-failed とし、native 資源が実際に終了しない場合は owner を先に破棄せず待機を続ける。

標準 Application は通常の所有者更新と入力配送前の準備結果確認を行う。差し替え候補の coordinator は commit まで無効であり、旧表示の清掃も所有者の binding 退役前に完了する。低水準 host では既存 manager と popup router を進めながら coordinator.Advance を呼び、Dispose 後も IsStopped まで同じ所有者上で継続する。

内容の role は既存 Group、操作は実際の Button と editor の能力で表す。Dialog の独立 surface は既存の診断 group の子 target として登録し、同じ排他的 input domain、Observe と OverrideInput の権限、および現在の modal probe を使う。新しい wire field や role は追加せず、schema 1〜9 の snapshot 形、typed client の版選択と既存 ancestor 検査を維持する。ラベル関係と通知 ID は受動的な公開 API の情報であり、wire snapshot に存在すると説明しない。

### 公開 API 差分

基準コードには以下の API がない。細かい生成 factory 引数と slot は通常の Composition に従い、主要な追加境界を抜粋する。

```diff
 namespace Lumyte.UI
 {
+    // 直接子の明示関係。name を推測せず、借用 editor を書き換えない。
+    public readonly record struct UiLabelAssociation
+    {
+        public UiLabelAssociation(int labelChildIndex, int editorChildIndex, string? accessibleName = null);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract partial class Widget
+            {
+                public UiLabelAssociation? LabelAssociation { get; init; }
+            }
+        }
+    }
+    public partial class UiElement
+    {
+        // 既定は許可。宣言を変更せず、子の pointer / wheel hit だけを絞る。
+        protected virtual bool AcceptsPointerTarget(UiElement target);
+    }
 }
 namespace Lumyte.UI.Controls
 {
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // Header、Content、Actions と任意 Close / Dismiss は通常の名前付き slot。
+            public partial class DialogContent : Widget { public DialogContent(); }
+            public partial class Toast : Widget { public Toast(); public Action? OnDismissRequested { get; set; } }
+            public partial class ToastHost : Widget { public ToastHost(); public required ToastController Controller { get; init; } }
+        }
+    }
+    public readonly record struct ToastId(Guid Value);
+    public sealed record ToastSnapshot(ToastId Id, bool IsVisible);
+    public sealed class ToastController : IDisposable
+    {
+        // capacity は表示中を含め 1〜1024。clock と host は借用する。
+        public ToastController(IMonotonicClock clock, int capacity = 16);
+        public ToastId Enqueue(Func<Widget> createContent, TimeSpan duration, Action<ToastId>? onClosed = null);
+        public ValueTask<ToastId> EnqueueAsync(IUiDispatcher dispatcher, Func<Widget> createContent, TimeSpan duration, Action<ToastId>? onClosed = null, CancellationToken cancellationToken = default);
+        public bool Dismiss(ToastId id);
+        public ValueTask DismissAsync(IUiDispatcher dispatcher, ToastId id, CancellationToken cancellationToken = default);
+        public void Clear();
+        public void Advance();
+        public IReadOnlyList<ToastSnapshot> CaptureSnapshot();
+        public void Dispose();
+    }
+    public static class UiRecipes
+    {
+        public static Widget IconButton(Widget icon, string accessibleName, Action? onClick = null);
+        public static Widget Card(Widget content);
+        public static Widget Card(Widget content, SRgba<float> borderColor, float borderWidth, float padding);
+        public static Widget FormRow(Widget label, Widget editor, string? accessibleName = null);
+        public static Widget ValidationMessage(Widget? message);
+        public static Widget Toolbar(IReadOnlyList<Widget> actions);
+        public static Widget StatusBar(IReadOnlyList<Widget> items);
+        public static Widget EmptyState(Widget message, Widget? action = null);
+        public static Widget PropertyField(Widget label, Widget editor, Widget? validationMessage = null, string? accessibleName = null);
+    }
 }
 namespace Lumyte.UI.Controls.Windowing
 {
+    public static class DialogCompose
+    {
+        // 値引数は UiArgument により通常の Signal を受ける。
+        public static Lumyte.UI.Windowing.Compose.Definitions.Dialog Dialog(
+            string key, UiArgument<string> title, UiArgument<bool> isOpen = default,
+            UiArgument<bool> isModal = default, UiArgument<DialogPresentation> presentation = default,
+            UiArgument<bool> closeOnEscape = default, UiArgument<bool> closeOnOutsidePress = default,
+            Vector2? initialClientSize = null, string? initialFocus = null,
+            Action<UiDialogCloseRequest>? onCloseRequested = null,
+            Action<UiDialogOpened>? onOpened = null, Action<UiDialogClosed>? onClosed = null);
+    }
+    public static class MenuCompose
+    {
+        // 子 Menu の新規内容を開くたびに作る。親 Menu の popup host を借用する。
+        public static Compose.Definitions.MenuItem Submenu(string key, Func<Widget> createContent,
+            bool isEnabled = true, string? accessibleName = null, Vector2? popupSize = null);
+    }
 }
 namespace Lumyte.UI.Windowing
 {
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // 内容は owner の通常 tree に接続しない portal slot。
+            public partial class Dialog : Widget
+            {
+                public Dialog();
+                public bool IsOpen { get; set; }
+                public bool IsModal { get; set; } // 既定 true。
+                public WidgetChildren Content { get; }
+            }
+        }
+    }
+    public enum DialogPresentation { Automatic, Hosted, NativeRequired }
+    public enum UiDialogCloseRequestReason { CloseButton, Escape, OutsidePress }
+    public enum UiDialogCloseReason { ModelClosed, Removed, OwnerClosed, Stopped, Failed }
+    public sealed record UiDialogCloseRequest(Guid DialogId, UiDialogCloseRequestReason Reason);
+    public sealed record UiDialogOpened(Guid DialogId, UiWindowId Window, UiWindowPresentation Presentation, string FallbackCode);
+    public sealed record UiDialogClosed(Guid DialogId, UiDialogCloseReason Reason, string Code, UiWindowPresentation? Presentation = null);
+    public sealed class UiDialogPortalCoordinator : IDisposable
+    {
+        // 公開 constructor は同じ manager に binding 済みの所有者を要求する。
+        public UiDialogPortalCoordinator(IUiWindowManager windows, UiPopupManager popups, UiPopupOwner owner, UiWindowId ownerWindow, UiSurface surface);
+        public bool IsStopped { get; }
+        public void Advance();
+        public void Dispose();
+    }
 }
```

## 検討した代替案

Menu の表示を modal として扱う方式は、通常の Dialog への focus 移動と所有者の編集終了を表さないので採用しない。既存 IUiWindowManager の Normal・Interactive 表示と、同じ入力 router 上の独立した許可範囲を使う。

内容ごとの別状態機械や OS 通知 timer は、合成と所有者 thread の規則を分岐させる。通常の Button、受動的な内容、既存 frame 境界へ分ける。AccessibleName を label の描画から推測する方式は、共有 editor の意味情報を書き換えるため、明示 metadata を使う。

Native Popup 能力だけで Native modal を許可する方式は、owner activation 抑止と解除を保証できない。現在は Hosted の実動作と NativeRequired の明示失敗に限定し、保証のない capability を公開しない。

## 結果と影響

アプリは内容と業務 callback を通常の合成で共有でき、Dialog と通知も同じ入力 lease・編集接続・表示寿命へ参加する。追加の責務は、通知制御器と時計の所有、任意 thread の dispatcher 指定、明示した文字組み・表示内容とラベル名である。

受け入れでは有限制約と slot 上限、複数表示面の独立状態、queue の容量・順序・新規内容・失敗保証、隠れた host の期限、callback の一回性を確認する。Dialog は pointer・wheel・key・candidate・Automation の背面遮蔽、入れ子、IME 終了、最新 focus 意図、閉鎖後の完全な drain、公開前の NativeRequired 失敗を確認する。

Native OS の activation・所有者抑止、物理 OS IME、Browser の実提示、GPU と他 RID の実行は、この Hosted と Headless の保証から独立した実環境の受け入れを要する。OS Native modal、drag・resize 装飾、結果 Task を返す Open サービス、OS accessibility bridge は本判断の範囲に含めない。
