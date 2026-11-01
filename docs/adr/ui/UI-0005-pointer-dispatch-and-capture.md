# ADR-UI-0005: Buttonへのポインター配送とcapture

- 状態: 採用
- 日付: 2026-10-27
- 対応: 実装PR 4（#80）、前提: #79（所有権は#77、layoutは#78）

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。入力hook、Receiver、capture、押下状態はElementへ移す。イベントの認識・伝播・取消規則は維持する。
過去の実装記録とElement移行後の検証記録を分けて記載する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

描画と同じ配置から入力対象を決め、Button内容上のクリック、領域外への移動、touch/penの取消を扱う必要がある。[UI-0003](UI-0003-layout-and-invalidation.md)のBoundsを使用し、GPU・描画実装に依存せず検証できるようにする。

## 決定

### 公開API

比較元は[UI-0001](UI-0001-composition-based-ui-system.md)に従う。Vector2はSystem.Numericsを使う。コールバック引数の生成はmain向け独立PRで提供する[COMPOSITION-0002](../composition/COMPOSITION-0002-callback-parameters.md)を前提とする。

```diff
+namespace Lumyte.UI
+{
+    public enum UiPointerPhase { Move, Down, Up, Cancel }
+    public abstract record UiInput;
+    // 位置はsurfaceの論理座標。Down/Upは主ボタンまたはcontactの開始・終了。
+    // record構築時には検査せず、Dispatch時に全成分finite・ID非負を検証。
+    public sealed record UiPointerInput(long PointerId, UiPointerPhase Phase,
+        Vector2 Position) : UiInput;
+    // hostのfocus喪失・配送停止時に全押下を取り消す。
+    public sealed record UiFocusLostInput : UiInput;
+    public sealed class UiSurface
+    {
+        // nullはArgumentNullException、負のID・非finite位置はArgumentOutOfRangeException。
+        // 通常入力の前にUpdateLayoutを成功させる。Dispatchは配置・入力情報を更新しない。
+        // 処理またはcapture配送した入力はtrue。未処理入力・未知pointer phaseはfalse。enum定義は検査しない。
+        public bool Dispatch(UiInput input);
+    }
+    public enum UiInputResult { Unhandled, Handled, StopPropagation }
+    // 値型。入力配送中だけ使用し、後で使うために保存しない。
+    public readonly struct UiInputContext
+    {
+        public UiElement Receiver { get; }
+        public UiInput Input { get; }
+        // captureによる配送か。所有を先に解除したUp/Cancelでもtrue。
+        public bool IsCaptured { get; }
+        public bool IsInside { get; }
+        // 非pointerはnull。clip前のElement原点からの位置。
+        public Vector2? PointerPosition { get; }
+        // Down/Move専用。それ以外はInvalidOperationException。
+        // 他ownerや不適格なreceiverではfalse。同Elementの複数pointerも許可。
+        public bool CapturePointer();
+        // 自身のcaptureだけを解除しCancelも通知。既に解除済みならfalse。
+        public bool ReleasePointerCapture();
+    }
+    public readonly record struct UiPointerCaptureState(long PointerId,
+        bool IsCaptured, Vector2 Position, bool IsInside);
+    public partial class UiElement
+    {
+        protected virtual UiInputResult OnInput(in UiInputContext context);
+        protected virtual void OnPointerCaptureChanged(UiPointerCaptureState state);
+        protected virtual IEnumerable<UiElement> GetInputChildren();
+        protected void InvalidateInput();
+    }
+    public class ButtonElement : UiElement<Compose.Definitions.Button>
+    {
+        public bool IsPressed { get; }
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public abstract class Widget
+            {
+                // ともに既定true。falseの部分木は入力対象外。描画は維持する。
+                [ComposeParameter] public bool IsEnabled { get; set; }
+                [ComposeParameter] public bool HitTestVisible { get; set; }
+            }
+            public partial class Button
+            {
+                // 既定null。生成ファクトリのonClick引数から設定する。
+                // 代入は置換、nullはハンドラーなし。認識完了時に同期実行する。
+                [ComposeParameter]
+                public Action? OnClick { get; set; }
+            }
+        }
+    }
+}
```

利用側は`Button(onClick: () => Save())[content]`と記述する。COMPOSITION-0002の生成契約を適用した、このPR時点の専用delegateを示す。引数順は既存Generatorの規則である派生メンバー→基底メンバーのordinal順を使う。

```diff
+namespace Lumyte.UI
+{
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            public delegate Button ButtonFactory(Action? onClick = null,
+                Optional<bool> clipToBounds = default, Optional<float?> height = default,
+                Optional<bool> hitTestVisible = default, Optional<bool> isEnabled = default,
+                Optional<bool> isVisible = default, Optional<float?> width = default,
+                IReadOnlyList<Action<Button>>? @with = null);
+        }
+    }
+}
```

省略・明示null、構築時にcallbackを実行しないこと、withと子インデクサーの順序は[COMPOSITION-0002](../composition/COMPOSITION-0002-callback-parameters.md)に従う。public event、購読リスト、OnClickを生成するComposeActionは追加しない。

構築後も`button.OnClick = handler`で置換、`button.OnClick = null`で解除できる。setterにthread・phase検査やlayout無効化の副作用は持たせない。surfaceの処理は[UI-0002の呼出し順序の契約](UI-0002-widget-tree-and-composition.md)に従う。OnClickがnullでもButtonの有効性・focus・capture・入力消費は変えず、操作を止めるにはIsEnabledを使う。複数処理が必要なら利用側が一つのcallback内で順序を定義するか、明示的に合成したdelegateを渡す。

### hit test

[UI-0003](UI-0003-layout-and-invalidation.md)で確定したBoundsを使い、[UI-0004](UI-0004-drawing-and-clipping.md)と同じ累積位置・clip規則で座標を合成する。入力処理で子のサイズを縮めたり、描画と別の座標計算を実装したりしない。

surfaceのviewport外は対象なしとする。各ノードの累積位置をBoundsから計算し、GetInputChildrenが返す列を逆順に走査してから自身を判定する。既定は全登録コレクションの子を登録順・各列内順で返し、Buttonは自身のChildrenだけを返す。ForegroundやEmbeddeds等を持つコンポーネントは入力対象と順序をoverrideする。基盤はBackground等のプロパティ名で判断しない。列は直接の子を重複なく返し、不正な列はUpdateLayout中にInvalidOperationExceptionとする。非表示・無効・HitTestVisible=falseの部分木は除外する。矩形は左・上を含み右・下を除く。幅・高さ0はhitしない。Fillの透明度で透過させない。

overflowした子も祖先clipとsurfaceのviewportを満たせばhitできる。clipなしの親の矩形内に探索を限定せず、点が親矩形外でも子を調べる。clipありなら祖先clipの積集合を先に検査する。最前面の要素を一つ選び、その要素から所有親へイベントを配送する。Buttonは自分がイベントを受けた場合に押下を認識する。内容中のButtonでは内側だけが対象となる。Buttonに属さない前面要素から背面のButtonへ入力を透過させない。

Down時には候補Button自身の矩形と祖先clip内であることも要求する。子がButton外へ描かれていても、その外側のDownだけでButtonを押せない。独自コントロールも共通のOnInputとcontextのcapture操作を使用する。

### Buttonの押下状態機械

surfaceに`pointerId → Widget`のcapture表、Button自身に`None / Pointer(id)`の押下ownerとinsideフラグを持つ。一つのButtonは一つのownerだけを持つ。IsPressedはownerあり、かつ最後の位置がButton矩形・viewport・祖先clip内のときtrueとする。Moveで領域外ならfalse、戻ればtrueになるがcaptureは維持する。

| 入力・変更 | 処理 | 戻り値 |
| --- | --- | --- |
| Down、対象なし | 状態変更なし | false |
| Down、空いているButton | ownerとcaptureを設定 | true |
| Down、既に同じIDがcapture中、または対象Buttonが使用中 | 再開始しない | true |
| Move、captureあり | capture先へ配送してinsideを更新 | true |
| Move、captureなし | hit要素へ配送。Buttonの既定処理は未処理 | handlerの結果 |
| Up、captureあり | owner・captureを先に解除し、最終位置が有効ならClick | true |
| Cancel、captureあり | Clickせず解除 | true |
| Up、captureなし | hit要素へ配送。Buttonの既定処理は未処理 | handlerの結果 |
| Cancel、captureなし | 何もしない | false |
| FocusLost | 全押下をClickなしで解除 | 状態を解除したときtrue |

このPRではキーボードfocusを保持せず、Downによるfocus設定は後続のUI-0006で追加する。UpdateLayoutの成功時に最後のpointer位置でinsideを再評価し、次のMoveがなくてもIsPressedを更新する。入力対象外になったcaptureは解除し、単なる位置変更では解除しない。宣言setterはcaptureを変更しない。surface Disposeは全captureを解除する。通常の値や子構成の変更は#77のElement照合で扱い、公開境界の取消を本PRで入力へ接続する。

### Elementが保持する入力情報と更新

入力対象の子列、有効な絶対矩形、祖先clipとの積集合は各Elementへ保持する。Element・UiSurfaceはButton/TextBox等の具体型を参照・判定しない。独立した入力snapshot、全ノードを平坦化した配列、Buttonから矩形へのmapを作らない。UpdateLayoutでlayoutを更新した後、または入力専用の無効化がある場合に、captureの有無によらずrootから入力情報を更新する。surfaceごとに更新識別子を発行し、今回選択されたElementへ記録する。前回までの識別子を持つElementは入力対象外とし、除外された部分木の古い情報を使わない。

GetInputChildrenは実行時slotのElement列を返し、無効化まで安定させる。標準のUiElementChildrenは所有権と重複検証を再利用して参照し、任意の独自列だけ更新時に検証する。入力用cacheもElementを保持し、同じ実体の宣言更新では付け替えない。runtimeの入力対象・順序・有効性・clip変更はInvalidateInput、geometry変更はInvalidateLayoutで要求する。宣言setterに無効化処理を追加せず、通常の値変更は再構築で扱う。

captureのないpointerはElementが保持した子列を前面から探索し、hit要素から所有親へOnInputを配送する。Element側がHandledまたはStopPropagationを返すまで一回ずつ呼ぶ。Elementがcaptureした場合も配送を止め、その系列を消費する。capture中のMove/Up/Cancelは保持したElementへ直接配送し、親へbubbleしない。Dispatchはlayout、入力情報の準備、無効化の解消を実行しない。入力情報の更新中に例外が発生したら更新を中断し、既に変更したElementの情報を戻さない。呼出し元は失敗した更新で描画・通常入力を継続せず、破棄または修正後の明示的なUpdateLayoutで再開する。旧状態を保つための作業snapshotや一括公開は設けない。

### Dispatchと取消

ホストは初回と表示状態変更後にUpdateLayoutを成功させてから通常入力を配送する。通常Dispatchは値検証→確定済み入力情報の参照→認識→状態解除→callbackの順に進む。作成threadへの固定やphaseの検査は行わない。pointer以外のUiInputも同じOnInputへ渡す。本PRではrootを配送先とし、focus対象へのキー配送はUI-0006、IME文書/sessionの接続はUI-0012で追加する。未知pointer phase、全Widgetが未処理とした入力はfalseを返す。Moveの間引きは行わず、Down/Up/Cancelを含む配送順を保持する。

Cancel、FocusLost、後続PRのUiKeyCancelInputはlayout・GetInputChildren・Clickを呼ばず所有状態を解除する。取消を受けるコンポーネントにはcapture喪失とCancelを通知する。CancelのPositionはfiniteを要求するが幾何には使わず、実位置がなければVector2.Zeroを渡せる。Cancelは当該pointer、FocusLostはsurface全体、UiKeyCancelInputは当該キーを対象とする。同じ取消の再送は冪等であり、更新失敗後も取消・Disposeを実行できる。ホストは描画と通常入力を停止した際にFocusLostで清掃し、消費済みの系列を物理終端までゲームへ転送しない。

### 部分木の入力領域による探索の打ち切り

各Widgetは自身の有効な入力矩形に加え、入力対象の子を含む部分木全体の軸平行矩形を保持する。UpdateLayout中の入力情報更新で、自身の入力矩形と今回の更新識別子を持つ子の部分木矩形を、子の更新後に合成する。非表示・無効・入力対象外の子の古い矩形は含めない。空矩形・clipによって交差が空になった矩形は合成から除く。

FindInputHitは点が部分木矩形外なら直ちに対象なしを返す。矩形内の場合だけ従来どおり子を前面から探索し、その後に自身を判定する。親の自身の矩形からはみ出した子も合成するため、overflowの入力契約を維持する。矩形は既存の祖先clipとviewportを適用した領域から求め、隙間等による過大な領域は許容するが実際の対象を含め損ねない。Button/TextBox等の具体型やhandlerの有無による最適化は行わない。

領域はElement内の追加の矩形フィールドとして保持し、独立したsnapshotや空間indexを作らない。Dispatchでは再計算せず、入力情報のみの無効化も次のUpdateLayoutで反映する。更新時はO(Widget数)の合成処理が増え、矩形分の常駐領域も増える。一方、hit testでは対象外の枝を一括で除外できる。重なる領域や多数の兄弟では最悪O(Widget数)の探索が残るため、変更前後の測定で効果と更新コストを確認する。

### コンポーネントによるイベント処理

UiSurfaceは入力先の選択・親への伝播・Widget単位のcaptureだけを管理する。ButtonのOnInputがDownでcaptureを要求し、MoveのinsideとUpでClickを認識する。単独owner・IsPressed・OnClickの取得はButton自身の責務であり、surfaceのcapture mapはWidgetを保持する。同じWidgetの複数pointer取得も可能とし、Buttonだけが自分の競合するDownを消費して追加captureを取得しない。

OnPointerCaptureChangedは取得・入力geometry更新・喪失を通知する。位置は最後の実入力位置であり、geometry更新のために物理Moveを偽造しない。Upではcaptureを先に解除して喪失を通知してからOnInputへ渡し、context.IsCapturedはtrueを保つ。Cancel・対象除外・明示解放・FocusLost・Disposeでは解除後に喪失とCancelを通知する。対象除外や取消はcallbackを直接実行しないが、独自コンポーネントの通知処理は呼ばれる。

通知からの同期再入に備え、全取消は対象集合を取り出して所有表を先に空にし、更新後の再評価は開始時点のcaptureだけを処理する。一つの通知が例外でも残りの取消を試み、全captureを解除する。単一の失敗は元例外、複数はAggregateException。Disposeは通知に先立って破棄状態にし、通知の失敗でも全Widgetを切断する。通常handlerがcapture取得後に失敗した場合も、そのhandlerが所有する系列を取消してから例外を返し、取消も失敗すれば両方をAggregateExceptionに含める。

TextInput等はOnInputでDownを消費してcaptureし、ローカル位置からcaret/選択を更新できる。ReadOnlyや一時的な編集不可でも外側Buttonへ渡したくないときはHandledで消費する。一般キー・文字列をButtonへのactivationへ変換せず、後続の型付きUiInputも同じhookで処理する。IMEの文書transactionとsession世代はUI-0012の別契約を維持する。

### Clickとcallback

認識完了時にowner/captureを先に解除し、その直後のOnClickをローカル変数へ一度取得して、非nullなら同期実行する。Down時点のdelegateを保持しないため、押下中に置換・解除したOnClickはそのUpから反映する。

callback内のOnClickの再代入・null指定は次回の認識から反映し、今回取得したdelegateを再取得・再実行しない。明示的に合成されたdelegateは.NETの呼び出し順と例外規則に従う。callbackではモデルを更新し、次の構築結果を作れる。Childrenの一回初期化は維持し、既存の子列を置換する入口は追加しない。明示的な同期再入をphaseで拒否せず、所有状態を解除済みなので次のDispatchを独立した入力として扱える。callback中にDisposeした場合も、callback後に対象へ内部操作を追加しない。layout・記録中の対象変更と明示的な並行処理は呼出し側の契約とする。

例外は伝播し、変更済みのユーザー状態はロールバックしない。owner/captureはcallbackより前に解除するため例外でも押下は残らない。取消やDisposeはOnClickを実行しない。独自コントロールには喪失・取消の通知が届く。マウス・touch・penの識別子とWindowの対応付け、入力消費のゲーム側への伝達はホストの責務とする。

## 検討した代替案

DownとUpを毎回hit testだけで配送すると領域外のUpで押下が残る。子要素だけを操作対象にするとButton内容上でClickできない。captureと最寄りButtonの規則を採用する。

public eventとComposeActionによる追加購読は、構築パラメーターによる設定・置換と異なる解除規則を必要とするため採用しない。nullableなdelegateプロパティと名前付き引数へ集約する。

## 結果と影響

描画なしで操作状態を検証でき、ポインター種別によらず同じ認識を使える。hover、wheel、筆圧、gestureはまだ公開せず、入力先の選択はUI本体の外に残る。

## 検証方針

- 重なり、透明前面、内容上、入れ子Button、端点、親外の子、clipとviewport、背景除外を確認する。
- 一方向layoutで親からはみ出した子のサイズを保ち、clipの切替で描画とhit testが同じ領域に制限されることを確認する。
- 外へ移動してUp、外から戻ってUp、Cancel、複数pointer、同一ID重複Downを確認する。
- 押下中の取り外し・無効化・非表示化・再配置・FocusLostでClickやcaptureが残らないことを確認する。
- 通常Dispatchがdirtyなlayoutや入力情報を更新しないこと、UpdateLayout失敗時に例外で中断すること、Cancel/FocusLostが更新を再試行せず清掃できることを確認する。後続のUiKeyCancelInputも同じ取消契約に従う。
- 実Generatorでlambda・method group・省略・nullを構築し、構築時に発火しないことを確認する。
- callbackの置換・解除、DownからUpまでの変更、実行中の自己置換、明示的なdelegate合成と例外、nullでも入力状態が残らないことを確認する。
- callback内のモデル変更・例外・次のDispatch・Disposeと、別threadでの順次利用を確認する。

## 実装・検証記録

2026-10-29、#80へ担当ADR・実装・テスト・README・pointer-onlyサンプルを追加した。delegate自動判別への変更後、UI 120件とComposition 70件がReleaseで成功した。Drawing2Dは既存99件の検証を維持する。ポインターcapture・clip・独自入力グループ・取消・callback例外・同期再入・Dispose・別threadでの順次利用を検証した。

6パッケージをpackし、ProjectReferenceを使わないNuGet consumerでlambda/method group、独自Composition slot、継承factory、ポインター配送・取消を実行した。pointer-onlyサンプルはGPUなしで一回のClickと押下解除を確認した。Windows/Browser入力source、DPI、キーボードfocus、標準hostとreconcilerの実装は各後続PRの範囲である。

過去の入力snapshot版ではUI 125件がDebug/Release、Drawing2D 99件がReleaseで成功した。定常Moveの割当0B、input-only無効化の失敗と再試行、遅延した子初期化、foreignな所有コレクション、viewportが空の場合のcapture維持を追加検証した。

Element内保持・明示的UpdateLayoutへの変更後はUI 127件がDebug/Release、Drawing2D 99件がReleaseで成功した。Dispatchが入力準備を行わないこと、再接続で旧入力情報を使わないこと、layout例外で途中結果を巻き戻さないことを確認した。

過去の入力snapshot版とElement内保持版の性能測定、条件・生データは[測定記録](../../performance/ui-input-snapshots.md)にまとめる。

WidgetからButton固有の参照・判定を除いた先行変更では、UI 128件がReleaseで成功した。独自入力コンテナーの子から外側Buttonを選択し、入力対象を内側Buttonへ切り替える場合も確認した。

汎用OnInput・capture通知への変更後、UI 142件がReleaseで成功し、先行の141件はDebugでも成功した。Button内の編集コントロールによる入力消費、ローカル座標、複数pointer、伝播停止、明示解放、更新通知中の再入、通知失敗時の全取消・Disposeを確認した。

汎用イベント版commit `b4595fd7d794984e61f557209d3e25e28d9ef1d4` を再測定した。10,241要素でcapture中Moveは約0.017µs・内部割当0B、captureなしMoveは約192µs・0B、入力情報更新は約0.495ms・64B、layout込みは約4.49msだった。captureなしのツリー探索は8,000件/sの時間予算を超える課題として残す。条件・比較対象・全batchは上記の測定記録へ追加し、今回は製品コードの最適化を行っていない。

部分木矩形による探索打ち切りを追加し、UI 147件がDebug/Release、Drawing2D 99件がReleaseで成功した。正負方向のoverflow・0寸法の親・空のclip領域・layout再配置・入力対象の切替・前面の遮蔽を検証した。

部分木矩形版commit `0c47a95e47da314e7fab4f35f6f32f4785d18919` と変更前を同じ19ケースで再測定した。10,241要素のcaptureなしMoveは約183µs→16.3µs、内部割当は0Bを維持した。入力情報更新は約0.477ms→0.553msで64B/回を維持した。入力中に更新処理を追加せず、探索の打ち切りだけを追加している。条件・全batchは測定記録を参照する。

### 共通の再構築基盤との接続

再構築はUI-0002のElement照合を使う。同一Elementで入力適格性が保たれる場合、captureと押下状態をそのまま継続し、新しい設定のcallbackを使う。RetainsInputOnRebuildによるWidget間の転送は廃止する。除去・型/key変更・不適格化ではCancelを配送し、取消時には状態を参照可能なままにする。独自の設定変更で継続できない部品はPrepareUpdateのcontextへ取消を要求する。

## Element基盤への移行記録

2026-10-31、#80でReceiver、入力geometry、hit test、bubbling、pointer captureをElementへ移した。
ButtonElementが押下状態と認識を持ち、現在のWidgetのOnClickを呼ぶ。状態のコピーと
RetainsInputOnRebuildによる参照転送は廃止した。PrepareUpdateのCancelInputで独自の取消を予約できる。
Release UI 177件で、再構築中のcapture継続、最新callback、保持祖先の無効化、
独自取消、取消中のDispose、無効な子選択、snapshotを作らない通常入力の動作を確認した。
子追加のテストは構築し直した宣言の公開へ接続し、接続中の初回Initializeによる暗黙更新を廃止した。
