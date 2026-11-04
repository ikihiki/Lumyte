# ADR-UI-0022: ページ内に宣言するDialogのNative/Hosted表示とモーダル入力scope

- 状態: 提案
- 日付: 2026-10-29
- 対応: PR #88のC8詳細設計。実装・検証はC8のDialog導入PRに同梱する
- 前提: UI-0011、UI-0013、UI-0014。key付き再構築・Signal・animationは各対応PRの実装後に接続する

## 背景

確認画面や設定画面では、ボタンを押したページ内からウインドウ風のDialogを表示したい。[UI-0013](UI-0013-component-catalog-and-delivery-plan.md)は内容Widgetとmodal hostを分けているが、利用者が表示状態をどこに指定し、focusや閉鎖要求をどう扱うかが未確定だった。

Webのコンポーネント型Dialogのように、内容と表示状態を通常のUI構築で宣言できるようにする。anchor付きPopup、アプリが管理する独立Window、ページが所有するDialogを区別する。Dialogの内容・開閉の宣言を変えず、Hostedと独立Native Windowを選べるようにする。Browserでも同じAPIで動作し、利用者にOS Windowやmanagerの生成・更新・破棄を要求しない。

## 決定

### 範囲と依存方向

任意のLumyte.UI.Controls.Windowingへ非表示のportal宣言`Compose.Definitions.Dialog`と利用者向けの静的factory`DialogCompose.Dialog`を追加する。内容の装飾・Header/Content/Actionsを組む既存候補はLumyte.UI.Controlsの`Compose.Definitions.DialogContent`へ改名する。DialogContentは46種類の表示・操作部品の一つ、portal宣言Dialogは接続基盤として別集計とする。同名factoryのusing staticが衝突しないよう、内容と表示を別名にする。

Controls.WindowingはControls、UI.Windowing、UIを参照し、UI本体へWindow/Platform/DI/Diagnosticsの逆依存を追加しない。描画binding/geometryは注入されたIUiWindowManagerのNormal/Interactive表示へ接続する。Hosted生成とNative表示先の登録を同じmanagerへ接続し、Nativeの生成/owner制御はhostの任意native adapterが担当する。Modalの遮蔽とfocus scopeはDialog coordinatorが担当し、UiPopupManagerのMenu modeをmodalの実装として流用しない。

DialogはHostedまたは独立Native Windowへ表示でき、anchorを必要としない。表示方式にかかわらず宣言したページが寿命を所有し、ページから独立した文書Window等はApplicationのWindow宣言を使う。一般のOS/アプリ全体を遮蔽するsystem modal、DOM dialog要素の生成、ページ間のDialog移送、結果Taskを返す命令的Openサービスは初期範囲外とする。

### 利用者向け公開API

比較元はPR #88の`6ce7cda30507644a9b62122a89f67c231bb41ae3`とUI-0013の未実装候補API。以下は追加/改名する主要宣言であり、実装済みのsignatureではない。WidgetはLumyte.UI.Compose.Definitions.Widgetの略記。factoryは通常の静的関数として呼び出せ、Contentは一回初期化の宣言slotとする。

```diff
+using Lumyte.UI.Windowing;
+
+namespace Lumyte.UI.Controls.Windowing
+{
+    // AutomaticはNativeを優先し、表示前の能力不足/安全に回収できた失敗はHostedへ。
+    public enum DialogPresentation { Automatic, Hosted, NativeRequired }
+    public enum UiDialogCloseRequestReason { CloseButton, Escape, OutsidePress }
+    public enum UiDialogCloseReason { ModelClosed, Removed, OwnerClosed, Stopped, Failed }
+    public sealed record UiDialogCloseRequest(Guid DialogId, UiDialogCloseRequestReason Reason);
+    // 実際に選ばれた表示方式と、Hostedへfallbackした固定理由codeを返す。
+    public sealed record UiDialogOpened(Guid DialogId, UiWindowId Window,
+        UiWindowPresentation Presentation, string FallbackCode);
+    public sealed record UiDialogClosed(Guid DialogId, UiDialogCloseReason Reason, string Code,
+        UiWindowPresentation? Presentation = null);
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // 非表示のportal宣言。内容をownerの通常の子として接続しない。
+            public partial class Dialog : Widget { public Dialog(); }
+        }
+    }
+    public static class DialogCompose
+    {
+        // titleは意味情報の名称。文字描画には内容側のText/providerを明示する。
+        // initialClientSize省略時は内容を有限制約でlayoutして初期寸法を決める。
+        // 初期focusはDialog内容内の一意key。未指定は最初のfocus候補。
+        public static Compose.Definitions.Dialog Dialog(string key, string title,
+            bool isOpen = false, bool isModal = true,
+            DialogPresentation presentation = DialogPresentation.Automatic,
+            bool closeOnEscape = true, bool closeOnOutsidePress = false,
+            bool isDraggable = false, bool isResizable = false,
+            System.Numerics.Vector2? initialClientSize = null, string? initialFocus = null,
+            Action<UiDialogCloseRequest>? onCloseRequested = null,
+            Action<UiDialogOpened>? onOpened = null, Action<UiDialogClosed>? onClosed = null);
+    }
+}
 namespace Lumyte.UI.Controls
 {
     public static partial class Compose
     {
         public static partial class Definitions
         {
-            public partial class Dialog : Widget { public Dialog(); public Action? OnCloseRequested { get; set; } }
+            // 表示/遮蔽を持たない内容Widget。既存Header/Content/Actionsのslot契約を維持する。
+            public partial class DialogContent : Widget { public DialogContent(); }
         }
     }
 }
```

titleは非null・非空、keyは同じ兄弟slot内で一意とする。initialClientSize指定時は正の有限寸法を要求する。initialFocusが接続時に見つからない/非操作可能なら最初のfocus候補へ移り、それもなければhostのDialog scopeをfocus targetとする。初期focus指定の重複keyは候補宣言の公開前に拒否する。callbackを保持するpublic eventや、利用者が呼ぶShow/Advance/Disposeは追加しない。

通常のWidget定義から生成する値/slotと、DialogComposeの薄い静的façadeで引数を渡す。assemblyを跨いだpartial Composeの共有は行わない。Signal対応後は値引数のisOpen/isModal/title/presentation/closeOnEscape/closeOnOutsidePressへUiArgumentを適用し、`Dialog(isOpen: openSignal)`を受ける。key、初期寸法、initialFocusと移動/resize設定は構築値とする。Signalの購読と構築contextはライブラリが管理し、Bind方式を追加しない。

### 利用例と表示状態

```csharp
using Lumyte.UI.Controls.Windowing;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Windowing.DialogCompose;

// openはUI owner上で一回作成し、モデル寿命で保持するSignal<bool>。
return Panel()[
    Button(onClick: () => open.Value = true)[Text("詳細を開く")],
    Dialog(key: "details", title: "詳細", isOpen: open,
        presentation: DialogPresentation.Automatic,
        onCloseRequested: _ => open.Value = false)[
        Stack()[
            Text("詳細の内容"),
            Button(onClick: () => open.Value = false)[Text("閉じる")]
        ]
    ]
];
```

Textは任意のMedia/ControlsのComposeもusing staticし、必要な文字layout provider/fontを明示して構築する。例は構文の要点を示し、未実装Textの引数を確定するものではない。Signal導入前もboolを渡し、モデル変更後に通常の構築関数を再実行する形で同じ表示意図を表せる。DialogContentは任意の内容部品であり、上例のStackのように独自内容だけを渡してもよい。

ボタンのcallbackはモデルのisOpenをtrueにするだけでよい。Dialogはowner側では寸法ゼロのportalであり、ownerのclip/layout寸法に参加しない。内容宣言は表示準備時に独立UiSurfaceへ一回接続する。owner surfaceとDialog surfaceへ同じWidgetを二重接続したり、接続済みの子列を書き換えたりしない。Dialog内でPopup/Tooltipや別Dialogを宣言する場合は、その表示scopeを論理ownerとして保持する。

状態はClosed→Preparing→Open→Closing→Closedとする。同じkey/型の再構築はsessionとfocus/入力状態を保持し、内容は新しい宣言をreconcileする。title/callbackの更新だけで再Openしない。移動/resizeした位置・寸法はsessionの表示状態として保持し、再構築で初期値へ戻さない。false/宣言除去でClosingへ進み、再追加は新しいDialogIdとする。Closing中にtrueへ戻っても旧sessionを復活させず、旧清掃後に最新宣言から準備する。失敗した同じtrue宣言は毎frame再試行せず、false→trueまたは新keyで再試行する。

isOpenの正本はアプリのモデルである。Escape/外側press/hostの閉じるボタンはonCloseRequestedを一入力系列一回通知し、モデルを書き換えない。callbackがfalseへ更新しなければDialogは開いたままとする。closeOnEscape=falseは要求を発生させず、モーダルのEscapeを背後へ流さない。closeOnOutsidePress=falseでもモーダルの背景pressは消費する。保存/取消等の業務結果はモデルと通常callbackで扱い、onClosedへ保存成功を暗黙合成しない。

### 表示方針とNative/Hostedの切替

presentationの既定はAutomaticとする。AutomaticはUiWindowPolicy.PreferNative、HostedはHostedOnly、NativeRequiredはRequireNativeと同じ選択方針で、Dialog hostが能力を確認する。Browser/Headless、Hostedだけの独自manager、Native owner操作に必要な能力がないhostでも、同じDialog宣言と内容でHostedを使える。NativeRequiredはHostedへ置き換えず、native-unavailableまたはmodal-owner-unavailable等の固定codeをOnClosedへ返す。

Nativeの必要条件は、所有付きの活性化可能な通常Window、表示前のhidden準備、取消可能なClosing/Closed観測、固有InputWindowId、独立した描画source/DPI、入力系列の取消、初回提示の確認、終了時GPU保持の回収である。closeOnOutsidePress=trueには別Windowへのoutside press観測も必要とする。isModal=trueでは対象owner scopeの入力/activation抑止と確実な解除も要求する。isDraggable/isResizableの指定をNative装飾へ適用できない場合も能力不足として扱い、指定を黙って無視しない。PR #86の非活性Popup対応だけでこれらを満たすとは判定しない。

Nativeの生成はApplication等のhost/driverが担当し、IUiWindowSourceとしてmanagerへ借用登録する。Controls.WindowingへPlatform/WindowSystemの直接参照を増やさず、低水準埋め込みhostも同じ任意native adapterを供給できる。登録/bindingと内容surfaceはDialog host、実Native Window/sourceは生成したadapterが所有し、leaseとGPUが返る前に実Windowを破棄しない。Hostedは既存managerで生成する。Native adapterのない独自managerを使用してもHostedのAPI/寿命は成立する。

Automaticのfallbackは、Nativeが一度も可視になっていないことを確認できる準備段階だけで行う。部分生成した描画binding/source/Windowとowner抑止を完全に回収してから、同じDialog session/内容をHostedへ接続する。内容factory、業務callback、OnOpenedを二重実行しない。Native Showの完了が不明/遅延している間はHostedを並行表示せず、閉鎖と回収を待つ。回収失敗では資源を保持して清掃失敗とし、fallback済みと報告しない。準備の期限と遅延完了回収は既存Window managerの契約に合わせ、同期waitや別のOS modal message loopを起動しない。

初回表示後にNativeが失われても自動的にHostedへ移送せず、失敗として通常終了する。表示中にpresentationを変更した場合は明示的な置換とし、新規入力/IMEを停止→旧session/owner抑止/資源を完全終了→最新宣言から新sessionを開始する。closed状態での変更は次回openへ適用する。古いWidget/UiWindowId/Automation ID、focus bookmark、OS位置を新sessionへ移送しない。アプリの共有Signalやモデルは保持されるが、surface内だけの編集/選択状態を表示方式の切替で保持する保証はしない。旧OnClosed後に新OnOpenedを通知し、連続変更は最新方針へ集約する。

### モーダルと非モーダルの入力・focus

isModal=trueの遮蔽範囲は所属する通常Windowの論理client領域とし、他の独立WindowやBrowserページ全体を遮蔽しない。所属Windowを跨ぐドラッグやキー系列が存在する時は旧系列を通常のCancelで終え、次のDownを待つ。Dialogを開く直前にownerのfocus bookmarkを保存し、未完了pointer/key、owner内のMenu、IME composition/候補を停止する。IMEのRequestEndは既存の終了契約に従い、文字を意図せずcommitする独自処理を追加しない。停止と最初の提示幾何が確定するまでDialogへ新入力を配送しない。

モーダル表示中は本文・タイトルバーとその子Popupだけを操作可能にする。背景は描画を継続するがpointer/keyboard/focus/Automationの対象外とし、背景pressのDownからUpまでを消費してUI/Gameへ漏らさない。Tab/Shift+Tabは最上位Dialog内を巡回し、初期focus候補がなくても背後へ移らない。Menu用の矢印/Home/Endによる自動focus移動をDialog全体へ適用せず、内容のTextBox/List等が通常どおり処理する。

Hostedの表示順は通常内容/非モーダルDialog→モーダルのbackdrop→当該Dialog→当該Dialog由来のPopup/IME候補とする。NativeではOSの所有関係とactivation制御でownerより前へ表示し、子Popup/候補をそのNative提示rootに接続する。背後の別scopeに属するPopupを上へ残さない。入れ子のDialogは最上位だけを操作可能とし、閉じると直下の生存scopeへfocusを戻す。bookmarkのWidget/attachmentが失効していれば直下scopeの最初の候補へ復帰する。閉鎖後のUp/キー終端を復帰先へクリックとして配送しない。

isModal=falseではbackdropやfocus trapを作らず、通常画面とDialogを双方操作できる。クリックしたscopeをactiveな論理keyboard targetとし、背景操作で勝手に閉じない。closeOnOutsidePress=trueを明示した場合は要求を通知するが、非モーダル背景への本来の入力を二重配送/取消しない。閉鎖時に別scopeが既にactiveなら古いbookmarkへfocusを奪い返さない。isModalの変更ではowner入力を一旦静止させてscopeを切り替え、新しい遮蔽と提示幾何が確定してから入力を再開する。Nativeでは変更後のowner抑止能力を再評価し、満たせなければAutomaticは旧session終了後のHosted置換、NativeRequiredは失敗終了とする。

Native modalではownerの入力gateをShow前に閉じ、host/native adapterが所有Windowの操作・activationを抑止する。複数Dialogの入れ子は抑止tokenを重ね、一つのDialog終了で別Dialogの抑止まで解除しない。ownerがHosted通常Windowなら物理root全体をOS無効化して無関係な兄弟Windowまで遮蔽してはならず、論理ownerだけを抑止できるadapterでのみNative modalを許可する。実現できなければAutomaticはHosted、NativeRequiredはmodal-owner-unavailableとする。

Native DialogはPopupのPreserveFocusと異なり、初回表示時にそのWindowへOS focus/activationを移す。終了時は現在のactive scopeを確認してownerの入力抑止を解除し、Dialogがactiveだった場合だけ生存ownerへOS focusとUI focusを復帰する。別の独立Windowを利用者が選択済みならfocusを奪わない。OSによる閉じる操作もCloseButton要求へ変換し、モデルがfalseになるまで実Windowを勝手に閉じない。owner停止/強制喪失は要求応答を待たず終了する。

InputWindowId/UiWindowId/Deviceは取得元と提示rootの対応を維持する。Hostedだからoriginを省略せず、Native入力をownerのoriginへ置き換えない。未知の座標変換は既存router規則に従って取消す。isDraggable/isResizableはHostedの装飾入力またはNativeのOS装飾へ適用し、drag中は取得元と系列を固定する。初回位置はowner中央とし、Hostedはroot、Nativeは利用可能なmonitor領域へclampする。以後はsessionの位置/寸法を保持し、NativeのDPI変更はそのsourceの観測値を使う。通常の本文layoutはFlutter型の一方向制約のまま、親から子の返した寸法を書き換えない。

### host・終了・Diagnostics

Applicationの標準hostはDialog coordinatorを自動生成し、描画/入力/IME/Popupと同じ更新loopへ接続する。利用者はUseWindowsへDialogを登録しない。低水準UiSurface埋め込みではhost側が同じDialog機能を接続する。Dialog機能のないhostへ開いた宣言を接続した場合はdialog-host-unavailableとして公開前に失敗し、本文の通常の子として表示するfallbackはしない。特定型の特別扱いをWidgetやUiSurfaceへ追加せず、portal/入力scopeの共通接続契約を使う。

終了は新規入力/更新の停止→既存系列Cancel・IME/候補/子Popup/子Dialogの静止→必要なら非操作のexit→描画/GPUとAutomation/Diagnostics参加者のdrain→binding/surface/HostedまたはNative表示の解放→owner抑止tokenの返却とする。ownerページ退役、強制喪失、アプリ停止ではモデルの閉鎖応答を待たない。OnClosedは実資源と参加者の解放後、モデル/page所有物の最終解放前に一session一回通知する。callback例外でも清掃を止めず、hostの既存失敗集約へ接続する。

OnOpenedは可視化と初回提示の確認後に一回通知する。準備/表示失敗もOnClosedへFailedと固定codeを通知し、部分生成物を回収する。新規宣言の検証失敗では直前の表示を維持し、その失敗を有効なmodal表示として扱わない。Dialogの初回内容layout/表示準備の失敗はFailedとして閉じ、継続中のlayout/描画例外は既存hostの描画停止へ伝播させ、無効なgeometryを入力へ公開しない。GPU drain/清掃失敗では旧資源を保持してhostの再試行経路へ接続し、先にpage scopeや表示rootを破棄しない。

Dialogは独立したAutomation page/Diagnostics child targetとして公開し、所属Windowとcontrol group、sequence、heartbeatと物理入力domainの排他を共有する。操作は同じrouterとmodal判定を通す。モーダル背後のtargetを指定しても操作可能にせず、既存の非操作可能結果を返す。Dialog終了で旧IDを無効化し、同keyの再表示へ転送しない。意味情報にはDialog role、title、modal属性を持たせるが、OSアクセシビリティ公開まで完了したとは扱わない。

### 環境と検証

| 環境・能力 | Automatic | Hosted | NativeRequired |
| --- | --- | --- | --- |
| Desktop、Nativeの必要条件を満たす | 独立Native。表示前の失敗は完全清掃後にHosted | ownerの提示root内 | 独立Native。準備不能なら失敗 |
| Desktop、Nativeの能力不足 | Hostedと理由code | ownerの提示root内 | native-unavailable等 |
| Hosted owner、論理ownerだけのmodal抑止ができない | modal時はHosted | 当該論理Window内 | modal-owner-unavailable |
| Browser | 同canvasのHosted、browser-hosted-only | 同canvasのHosted | native-unavailable |
| HeadlessまたはHosted専用manager | 仮想/既存root内のHosted | 同じroot内 | native-unavailable |

遮蔽範囲はいずれも論理owner Windowとし、NativeのOS所有関係を理由に他の独立Windowを無効化しない。Browserではwindow.openを自動実行せず、canvas外DOMを遮蔽しない。Headlessの検証をOS focus/物理IMEの代替にしない。

受け入れ条件は、ボタン→表示→要求callback→モデル変更→終了、要求無視による表示継続、同key再構築と再追加ID、準備中の反転、非モーダルとドラッグ/resize、入れ子modal/focus trap/復帰、背景押下の系列消費、IME/候補/Popupとの順序、DPI/resize/clip、Diagnosticsから背景操作できないこと、子の清掃とGPU lease、Browserのcanvas外入力維持とする。さらに各policy、能力不足、Native hidden準備/Show不明/遅延完了、fallback前の完全清掃と内容一回、Native modal owner抑止と入れ子、論理Hosted ownerへの過剰抑止なし、OS close/focus復帰、方式の連続変更、Native/Hostedの同時表示なしを検証する。HostedだけのテストはNative実環境対応の実証にせず、Windows/Linuxの実Window試験を別途記録する。

## 検討した代替案

- Window宣言だけでDialogを表すとページの局所的な表示状態をアプリへ持ち上げる必要があるため、ページ内portalを提供する。
- UiPopupManagerのMenu modeだけでmodal化すると、矢印操作、Tab、IME、背景遮蔽がDialogの契約と異なるため、共通の描画/Window基盤を使い入力scopeは分ける。
- 命令的なDialogService.Openと結果Taskを主APIにすると、モデルのisOpenと生成handleという二つの状態管理が必要になるため、初期版は宣言とcallbackに統一する。サービス方式は必要性と所有権を別途検討する。
- Hostedに固定するとowner領域外へ出す独立表示ができないため、同じ宣言でNativeとHostedを選べるようにする。BrowserだけDOM dialogを使うと描画/入力経路が分かれるため、Browserのfallbackは既存Hostedを使う。

## 結果と影響

ページ内の構築関数とモデルだけでDialogを開閉でき、利用者による独立Window登録を要求しない。AutomaticはNativeの能力に応じてHostedへfallbackし、Native必須も明示できる。DialogContentとportalの分離により、見た目を通常のCompositionで組み立てられる。

一方、modalの入力scope、portal接続、入れ子のfocus/IME/診断と非同期退役をhostが実装する必要がある。本ADRは未実装の設計であり、既存PopupやWindowの試験をDialog対応の実証として扱わない。C8の導入PRに本ADR・実装・テスト・READMEを同梱する。

## 参考資料

- [UI-0011: Popupの表示先と寿命](UI-0011-popup-windowing-and-fallback.md)
- [UI-0014: 注入するWindow manager](UI-0014-injectable-window-manager.md)
- [PrimeNG Dialog](https://primeng.org/dialog): visibleと変更通知で開閉するコンポーネント型API
- [Angular Material Dialog](https://material.angular.dev/components/dialog/overview): サービスから開いて終了結果を受け取るAPI
- [Radix Dialog](https://www.radix-ui.com/primitives/docs/components/dialog): Root/Portal/Overlay/Contentを組み合わせるAPI

外部APIの型を移植せず、状態を引数とcallbackへ渡す考え方と表示/内容の分離を参考にする。Preact自身の標準Dialog APIとしてこれらを位置付けない。
