# ADR-UI-0019: UI本体によるSignal引数と購読寿命の自動管理

- 状態: 採用（PR #93で実装）
- 日付: 2026-10-30
- 対応: 実装PR 15、前提: SIGNALS-0001・COMPOSITION-0003・実装PR 1〜14

## 2026-10-31の責務境界更新

[UI-0002](UI-0002-widget-tree-and-composition.md)でWidget宣言とElement実体の分離を採用した。購読ownerとdirty境界はElement。同じsourceのobserverを継続し、宣言へsetterを適用しない。
旧revisionの実行記録は履歴として扱い、以下のElement分離後の再検証記録を参照する。
最終API・公開順・移行完了条件はUI-0002を正本とする。

## 背景

UIはusing staticから`Rectangle(width: widthSignal)`と構築でき、購読の登録先や実行機関を利用者が指定する必要がない形にする。同期の構築contextを開かせる方式は、静的な構築に管理手順を追加する。

UIライブラリがSignalへ依存し、Widgetの接続・取り外し・surface破棄に合わせて購読する。状態管理本体は[独立したSignals](../signals/SIGNALS-0001-independent-signal-runtime.md)のまま、UI側が値の適用と寿命を担当する。

## 決定

Compositionのgeneric値adapterと受動recipeはPR #98でmainへ統合済みである。本PRはUiArgumentとUI／Window／Popupへの接続を実装し、汎用の生成契約を重複して含めない。

静的関数だけで定義するWidgetと、その関数内のローカルSignal保持は、後続PR 18の[UI-0023](UI-0023-function-widgets-and-local-signals.md)で追加する。独立SignalとUIによる自動購読という本ADRの境界は維持する。

### 依存方向と配置

Lumyte.UIからLumyte.Signalsへの直接参照を追加する。実行時の直接依存はComposition、Input.Abstractions、Graphics.Drawing2D、Signalsとする。SignalsはBCLとMicrosoft.Extensions.ObjectPoolへ依存し、UI・Composition・contextを参照しない。UI専用のInternalsVisibleToをSignalsに追加しない。

Signal対応はUI本体のWidgetとUiSurfaceへ組み込み、通常のLumyte.UI.Composeをusing staticする。別のUI.Signals package、別factory群、専用UI Generator、UiSignalScope、EnterBuild、UseSignalsは公開しない。値の初期化と設定情報登録は[COMPOSITION-0003](../composition/COMPOSITION-0003-argument-adapters.md)のgeneric adapter契約を使い、Composition本体はUIやSignalsへ依存しない。

Application/Navigationは既存のsurfaceの接続・表示・終了を管理し、その中で自動反映の停止/再開と候補検証を接続する。UI本体からWindow manager、DI、Platform、Diagnosticsへの逆依存を追加しない。

### UI引数の公開API

比較元はmain `f83ebaf729ec284ae2185670ef10e224a1abce9c`と先行UI提案。以下は実装PR 15で追加するAPIである。Widgetの既存propertyは具体値のままとする。

```diff
+namespace Lumyte.UI
+{
+    public readonly struct UiArgument<T> : Lumyte.Composition.IComposeArgument<T>
+    {
+        // defaultは未指定。literalのdefault/nullと区別する。
+        public bool IsSpecified { get; }
+        public bool IsSignal { get; }
+        public T ConstantValue { get; }
+        public Lumyte.Signals.IReadSignal<T>? Source { get; }
+        public static UiArgument<T> Constant(T value);
+        public static UiArgument<T> FromSignal(Lumyte.Signals.IReadSignal<T> source);
+        public static implicit operator UiArgument<T>(T value);
+        public static implicit operator UiArgument<T>(Lumyte.Signals.Signal<T> source);
+        public static implicit operator UiArgument<T>(Lumyte.Signals.Computed<T> source);
+        // 以下はGenerator向けbridge。外部購読を開始しない。
+        public T GetInitialValue();
+        public void AttachTo<TTarget>(TTarget target, object memberKey, T initialValue,
+            Action<TTarget, T> setter) where TTarget : class;
+    }
+    public readonly record struct UiSignalRevision(ulong Connections, ulong Values);
+    public sealed class UiSurface
+    {
+        // 既存の(root) constructorは維持。hostはdispatcherを借用で指定できる。
+        public UiSurface(Compose.Definitions.Widget root, IUiDispatcher dispatcher);
+        public UiSurface(Func<Compose.Definitions.Widget> build);
+        public UiSurface(Func<Compose.Definitions.Widget> build, IUiDispatcher dispatcher);
+        public void Rebuild(Func<Compose.Definitions.Widget> build);
+        // owner Idleで読取る。値本体を含まず、prepared viewとの一致検証に使う。
+        public UiSignalRevision SignalRevision { get; }
+        // host向け。既定true。Pause中も明示UpdateLayoutで現在値を準備できる。
+        // 利用者のページBuildでは呼ばず、navigation/終了処理が管理する。
+        public void SetSignalUpdatesEnabled(bool enabled);
+    }
+    public static partial class Compose
+    {
+        public static partial class Definitions
+        {
+            // Generatorが継承先のmutableな非delegate引数にも適用する。
+            [Lumyte.Composition.ComposeArgumentAdapter(typeof(UiArgument<>))]
+            public abstract class Widget { }
+        }
+        // 生成APIの抜粋。実際の全引数/順番は元のparameter一覧に従う。
+        public delegate Definitions.Rectangle RectangleFactory(
+            UiArgument<Color2D> fill = default,
+            UiArgument<float?> height = default, UiArgument<float?> width = default,
+            IReadOnlyList<Action<Definitions.Rectangle>>? @with = null);
+        public static RectangleFactory Rectangle { get; }
+    }
+}
```

UiArgumentは未指定・constant・Signalの三状態を保持し、Optionalで重ねて包まない。GetInitialValueはconstantならその値、SignalならPeekによる現在値を返す。requiredの未指定は生成前に拒否する。mutableな非delegateのComposeParameterだけをadapter化し、init-onlyは構築時の値、callbackは元のdelegateの引数規則を維持する。

SourceのTとproperty型は完全一致させる。WidthならSignal/Computedの型もnullable floatとし、数値やnullable間の変換はComputedで明示する。interfaceからのuser-defined implicit conversionは定義できないのでFromSignalを使う。null sourceはArgumentNullException、constantでないConstantValue参照はInvalidOperationException。null literalの変換が競合する場合は`UiArgument<string?>.Constant(null)`等で明示する。

AttachToはmemberKeyと構築用の型付きsetterを使い、Widget内のprivateな引数記録へsource・初期値を登録するだけである。setterは新しい宣言の構築に使用し、接続済み宣言への更新配送には使用しない。targetが対応Widgetでない、null key/setter、重複keyは登録前に拒否する。constantの場合は記録しない。bridgeを通常のBind APIとして提供せず、生成コードから利用する。

### 静的な構築と未接続Widget

利用者は通常の`using static Lumyte.UI.Compose;`でPanel、Rectangle、Button、Button.Backgroundを呼ぶ。factoryはscope、dispatcher、current pageを探索せず、Signalの初期値から従来の具体値を設定する。withはparameterの後、子インデクサーはfactoryから戻った後という順序を維持する。

未接続Widgetが持つのは受動的なsource/初期値/setter情報だけで、Effectや配送予約を作らない。未接続中にsourceが変わった場合も、接続後の最初のUpdateLayoutまでに現在値を反映し、それが成功してから通常入力を配送する。構築途中の例外や放棄したtreeが、モデルのobserverから保持され続けることを防ぐ。Widgetが外部から参照されていなければ、記録したsourceとの通常の参照もGC対象になる。constructor/withの外部副作用をrollbackしたとは扱わない。

withが同じpropertyを上書きした場合は初回の優先順位を維持する。接続後のsource変更は新しい構築結果へ反映する。接続済み宣言のpropertyを直接書き換える更新方式は提供しない。sourceの差替え・解除や子構成の変更も、新しい宣言を照合する経路で扱う。

### 接続と動的な子の変更

UiSurfaceは変更するElementごとに、受動的な引数readerと構築関数の読出し集合を観測するEffectを準備する。購読を必要としないElementへ空のEffectを作らない。内部scopeを使い、接続を外側の業務SignalScope内で行ってもUI購読の所有権を利用者scopeへ移さない。Signalの生成threadとの一致を検査しない。標準hostは自身のIUiDispatcherへ配送し、UiSurface(root)のみの利用は従来どおりcallerがUI操作を直列化する。

初回Effectは依存と初期値との差だけを確認し、setterへ配送しない。構築関数を渡した場合はSignal.CaptureReadsで直接のValue読出しを記録する。明示Signal引数の初期値はPeekで読み、引数だけの変更を全Build依存へ混ぜない。再構築時は生成recipeから新しい宣言を作るか、直接読出しがあれば元の構築関数を再実行する。recipeはconstructor・withと全slotを保持し、古いWidgetへの参照連鎖は作らない。手書きWidgetはUI-0002のRebuildDeclarationで再生成できる。hookを実装しない手書きnewへ依存する全体再構築には`UiSurface(Func<Widget>)`または`Rebuild(Func<Widget>)`へ構築関数を渡す。PR 18の関数Widgetではbody自体を再実行するため、そのbody内の手書きnewは例外として個別recipeを必要としない。

構築の自動再実行は#77から実装済みのRebuild/UpdateTree・型/key/slot照合・component状態保持へ接続する。本PRではSignalの依存追跡・再実行の集約と購読寿命を追加し、状態保持の基盤を二重に実装しない。

子構成も[UI-0002](UI-0002-widget-tree-and-composition.md)の再構築で扱う。各宣言のChildrenと背景slotは一回だけ初期化し、reconcilerが継続する表示要素を対応付ける。新しい宣言の検証後・表示commit前に追加分の購読を準備する。準備失敗では直前の表示を維持する。commitでは削除分のEffectと予約を失効させ、追加分を接続する。表示要素の並べ替えだけでは維持できる購読を作り直さない。

取り外すElementの購読を解除し、遅い仕事を失効させる。Widget宣言の子列は書き換えず、
同じ型/key/slotで存続するElementの子と状態を保持する。同じ受動的な宣言を別Surfaceへ
mountする場合は独立Elementと新しい購読を作る。UiSurface.Disposeは全購読を解除し、
共有Signal/Computedと外部モデルを破棄しない。

WidgetのValidateDeclaration(UiWidgetValidationContext)は予定Elementツリーを検証する。
Popup keyと参照anchorの重複もこの投影で拒否し、存在しないanchorはlayout後まで未表示で待つ。

### 自動配送とlayout・描画

通常hostはUiSurfaceへ既存IUiDispatcherを渡す。UI schedulerはsurface単位のqueueへEffect仕事を集約し、一つのdispatcher予約でowner Idleへ戻す。投影Effectは毎回sourceを読んで変更をstagingするだけとし、UIの専用更新経路がCoreの評価区間を抜けた後に構築関数を再実行する。新しい宣言を照合し、表示要素のlayout・描画への影響を分類して反映する。接続済みWidgetのsetterを更新経路に使わず、購読準備もCore Effectへ再入して実行しない。Coreの通常Effectと別viewの投影を勝手にpumpせず、利用者へschedulerや手動Flushを要求しない。取り外し・DisposeではEffectを破棄して遅い仕事をno-opにする。Pauseは適用を停止し、依存の変更通知とrevisionの観測を維持する。

既存のUiSurface(root)だけで利用する場合もUIが管理する。通知は仕事queueとdirty Element集合へ集約し、UpdateTree / UpdateLayout / Record入口とDispatch完了後の安全な更新点で処理する。dispatcherがある場合はそのowner予約でも処理する。dispatcherなしでSignalを更新しただけでは、最後に公開したWidget設定は変わらない。Dispatch内で表示geometryや入力情報の更新を実行しない。Layout/Record中には新しい表示をcommitせず、通常のUpdateLayout/Record入口で必要な保留値を反映する。標準hostは通常入力の前に更新を済ませる。標準hostでは別threadからのSignal更新もdispatcherへ予約する。dispatcherを指定しないsurfaceのUI操作の同時実行はcallerの責任とする。

初期接続・再開・明示UpdateLayoutでは、source値とEffect.Revisionを採取してから必要な再構築・表示更新を行う。値は前回適用値と標準比較し、同値ならその依存による更新を要求しない。表示への反映ではモデルへ書き戻さず、書戻しは入力callbackで行う。準備中の外部副作用等でRevisionがさらに変わればprepared viewを最新として扱わない。

SignalRevisionは接続集合の世代と、その集合に属するEffectの変更stampから成る。値のstampは全Signal間で比較可能とし、Pause中・既にdirtyの更新も検出する。集合の追加/削除はConnectionsを進める。snapshotの一致で準備済みlayout/描画との対応を検証し、生のモデル値を公開しない。

layoutとRecordは採用済みWidgetの具体値を読み、Signal値を描画中に再評価しない。
非同期完了等でsourceが途中更新されても、次のUI更新点まで同じ設定で現在のframeを記録できる。
source revisionの変化だけを理由に通常の描画を失敗させない。既存のprepared viewは
最後に適用したCommittedSignalRevisionを保存し、commit時に保留通知も含むSignalRevisionと
比較する。これにより古い候補を拒否しつつ、通常の非同期更新で描画を停止しない。
無効化は宣言setterで行わず、予定する設定差分をElementのCompareConfigurationで分類する。
Width/Height等はlayoutを、Fill等は描画だけを無効化する。Flutter型layoutと全画面記録を維持する。

同期の構築・接続・初回反映エラーはcallerへ返す。後続Effect/配送エラーはsurface内部へ保持し、次の通常UI操作またはhostの検査で報告する。標準hostはstate-update-failedとして停止へ接続する。構築中のユーザーコードの外部副作用をrollbackせず、標準hostではdispatcher外でUI callbackを呼ばず、未観測Task例外を残さない。

### navigation・Window・IMEとの連携

[UI-0017](UI-0017-navigation-and-page-lifetime.md)の標準hostは候補surfaceの自動配送を停止し、通常のlayout/描画準備で初期値を反映する。prepared bindingにはSignalRevisionも保存し、TryValidateCommitで現在値と比較する。準備後に変更があればcandidate-staleとして旧viewを保つ。commit区間でuser codeやSignal再評価を呼ばず、直後のIdleで自動配送を再開する。

旧viewのquiesceではSetSignalUpdatesEnabled(false)で投影EffectをPauseする。rollbackでは通常UpdateLayoutで最新値を準備して再開し、再提示済みgeometryを確認してから入力gateを開く。commit済み旧viewは再開せず、GPU退役後のsurface解放で購読を返す。app停止、Popup、Hosted/Native windowもそれぞれのsurface/bindingの寿命に従い、別viewや共有モデルの購読を停止しない。

原則としてWidgetの移動や取り外しは接続世代を変更し、古いWindow/Surfaceを捕捉した予約を新しい接続へ流さない。SignalはWindowを所有せず、既存のWindow ID/Input origin/IME候補契約を維持する。

モデルへの書戻しは名前付きcallbackで行う。TextBoxはstringのText（またはそのSignal値引数）を受け、OnTextChangedでモデルを更新する。内部のTextDocumentはUIが保持し、[UI-0012](UI-0012-ime-and-text-editing.md)の変換・selection・編集session契約を維持する。IME変換中に文書全体を置換したり、暗黙の確定や双方向同期を実行しない。

### 任意の業務Effectの寿命

UI引数の購読は完全に自動管理し、context拡張を前提にしない。ページ固有の業務Effectをまとめて解除したい場合だけ、次の任意の所有権接続を提供する。

```diff
+namespace Lumyte.UI.Application.Navigation
+{
+    public sealed class UiPageContext
+    {
+        // page所有物だけを登録。重複・DI所有aliasは拒否する。
+        public void Own(IDisposable resource);
+    }
+    public static class UiPageSignalExtensions
+    {
+        // 任意の業務Effectグループを作りOwnする。Signal値は所有しない。
+        public static Lumyte.Signals.SignalScope CreateSignalScope(this UiPageContext context);
+    }
+}
```

Lumyte.UI.Application.Navigationはこの任意拡張を提供する。Ownはdispatcher上の有効なPrepare/Buildで登録し、未公開/途中失敗でも保持する。page退役・surface解放後、DI scopeの前にowner上で逆順に一回だけDisposeする。最終Dispose失敗はterminal cleanup failureとして保存し、標準DIの再試行を仮定しない。共有モデルの業務Effectはその所有者が独立して解除する。DI登録型（interface登録を含む）のinstanceとサービスprovider/scopeはOwnで受理せず、解決を追加実行して所有権を推測しない。同型を手動生成した場合も保守的に拒否する。重複登録と退役後の登録は拒否する。

### 利用例

```csharp
using Lumyte.Signals;
using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;

// IUiPage.Buildの内容。UI管理のためのscopeやruntimeは不要。
var count = Signal.Create(0);
var widthSignal = Signal.Computed<float?>(() => 80f + Math.Min(count.Value, 10) * 8f);
return Panel()[
    Rectangle(width: widthSignal, height: 24f,
        fill: new Color2D(0.2f, 0.4f, 0.8f, 1f)),
    Button(width: 160f, height: 40f, onClick: () => count.Value++)[
        Button.Background()[Rectangle(fill: new Color2D(0.3f, 0.3f, 0.3f, 1f))]
    ]
];
```

FromAsyncの非同期結果は処理identity・取消を確認してSignalへ公開し、UIの観測は既存schedulerがownerへ配送する。手動で取得したデータも従来どおりSignalへ代入できる。UIと独立したSignalを保持するモデルも同じ引数へ渡せる。通常利用者はruntime、Bind、EnterBuild、UI購読scopeを管理しない。

### Animationへの表示値の接続

実装PR 16の[UI-0020](UI-0020-animation-and-frame-lifetime.md)でUiAnimated引数とTimelineを追加する。目標値はモデルSignalのまま、Animationの補間結果をUI所有の表示Signalへ一括反映する。producerはsurface接続transactionへ含め、model target監視のstampもSignalRevisionへ含める。購読・frame・表示休止はUIが管理し、通常Composeで構築する。Signalsの公開API/依存は変更せず、UiAnimatedの変換には所属を維持するSelectを使う。

### PopupとWindowのSignal引数

2026-10-29追記。[UI-0011のPopup](UI-0011-popup-windowing-and-fallback.md#宣言的なpopupの利用者api)と[UI-0016のWindow](UI-0016-ui-application-framework.md#複数windowの宣言api)も同じ「値を引数として受ける」方式へ接続する。本PRで再構築/引数adapterと同時に実装する。

Popupは通常のCompose.Definitions.Widgetを継承し、isOpen/placement/presentationへ`UiArgument<T>`を適用する。key/anchorは構築ごとの識別値、callbackはActionのままとする。`Popup(isOpen: menuOpenSignal, ...)`をusing staticから直接呼び、専用Bind/Openや構築scopeを要求しない。popupのportal slotはowner側宣言に属し、内容の接続/購読はPopup専用surfaceが担当する。ownerへ二重接続せず、同keyの継続では内容の構築結果を専用surfaceの通常reconcilerへ渡す。ownerの宣言除去でPopupの新規入力/更新を止め、GPU等の退役完了まで旧surfaceを保持する。

非WidgetのWindow declarationもApplication側のfactoryでtitle/isOpenを`UiArgument<string>`/`UiArgument<bool>`として受け、受動的なsource記録を保持する。これはUI.Argument adapterのWidget限定AttachToを非Widgetへ呼ぶ拡張ではなく、公開UiArgumentのIsSpecified/Source/GetInitialValueを読むApplication固有の引数処理とする。キー/owner/初期寸法/方式/内容factory/callbackは構築値として扱い、動的変更はWindow一覧の構築関数を再実行する。Window factoryの具体値引数をadapter引数へ置き換え、boolとSignalの競合overloadを増やさない。

ApplicationはUseWindowsの構築関数内で読まれたSignalと、返った宣言のtitle/isOpenのSignal引数を自動購読する。前者は一覧構成の再構築、後者は該当宣言の再評価をownerの次の更新点へ集約する。構築評価区間を抜けるまでOS/Window操作を行わず、現在の宣言を読みながら同時に破棄しない。keyが変わらなければWindow/content/navigationを再生成しない。effect/scheduler/scopeはhost内部に閉じ、利用者はモデルにSignalを保持すればよい。

```csharp
// Signal.Createはモデルの初期化時に一回実行する。
var inspectorOpen = Signal.Create(false);

// UI内のPopup
Popup(key: "file", anchor: "file-button", isOpen: menuOpen,
    onDismiss: _ => menuOpen.Value = false)[BuildMenu()];

// Application内の独立Window
Window(key: "inspector", title: "プロパティ", owner: "main",
    content: Page(inspector), isOpen: inspectorOpen,
    onCloseRequested: _ => inspectorOpen.Value = false);
```

初回購読の確立時には解決済みのtitle/isOpenと現在値、一覧構築のread revisionを照合する。構築から接続の間に変更があれば再構築を予約し、接続前の通知を取りこぼさない。

Window宣言のsource購読は閉じた宣言にも必要であり、isOpen=falseでもその宣言がUseWindowsの結果に存在する間は保持する。そうしないとfalse→trueを検出できない。一覧から除去した宣言の引数購読は解除し、一覧構築effectの依存は次の構築で読み直す。停止時は全予約を失効させ、遅いSignal仕事が解放済みWindowや新instanceへ届かないようにする。共有モデルのSignal自体はhostがDisposeしない。

Window一覧の構築関数内でSignal.Createを毎回呼ぶことは状態保持の契約ではない。モデルはアプリ/ページの寿命に保持し、通常のboolや任意サービスの変更監視は行わない。Signalを使わない一覧の変更にはRequestRebuildWindowsを使える。dismiss/close callbackからモデルを更新しても同期再入せず、表示の反映は次の更新点で行う。

検証には、Signalを直接渡した開閉/title更新、Read依存による文書Window一覧の増減、閉じた宣言の再表示、不要なWindow再生成なし、callback中の更新集約、未公開宣言の購読漏れなし、除去/停止後の通知失効、Popup内容とownerの二重接続なしを含める。

### ページ内DialogのSignal引数

[UI-0022](UI-0022-declarative-hosted-dialogs.md)のDialogも通常の静的構築から`Dialog(isOpen: openSignal, ...)`と呼べる。isOpen/title/isModal/presentation/closeOnEscape/closeOnOutsidePressをUiArgument化し、callbackは元のActionのままとする。Bind、Dialog専用runtime、利用者の構築scopeは追加しない。

isOpenの購読はowner側のportal宣言の寿命へ接続し、falseでも宣言が存在する間は維持する。内容側のSignalは開いたDialogの専用surfaceが購読し、閉鎖/取り外しで解除する。二つのsurfaceへ同じWidgetを接続せず、モデルSignal自体はDisposeしない。onCloseRequestedからのSignal更新は次の更新点へ集約し、Dispatch中にscope/表示geometryを変更しない。

宣言keyの継続ではsession/内容状態を照合し、title等の値変更で再Openしない。presentationの変更はUI-0022の明示置換として旧Native/Hosted sessionとowner抑止を完全終了してから最新方針で再開し、共有モデルSignalを保持する。連続変更は次の更新点で集約し、閉鎖中の旧surfaceへ反映しない。宣言除去/ページ退役で閉鎖した後の通知は世代で失効させ、再表示したDialogへ転送しない。本節はC8のDialog実装と接続する追加設計であり、Signal PRだけでDialogを実装済みとは扱わない。

### async関数の結果とUI寿命の接続

非同期変換本体のAPI・状態遷移・競合処理はmainへ統合済みのPR #99の[SIGNALS-0002](../signals/SIGNALS-0002-async-factory-conversion.md)を正本とする。UI側はAsyncSignalを通常のIReadSignalとして読み取り、既存のdispatcherへ通知を接続する。

ページではPrepare等で`context.CreateSignalScope().Run(...)`内に作るか、`context.Own(Signal.FromAsync(...))`でページ寿命へ接続する。再構築される関数bodyで毎回処理を開始せず、所有者の初期化時に作る。汎用Useは自動破棄を担わないため、Useだけで非同期処理の寿命を管理しない。

## 検討した代替案

- UIをSignals非依存に固定すると利用者へadapterや構築scopeの設定を要求する。UI側の依存を許可し、自動管理を組み込む。
- 静的factoryが直ちに外部購読すると放棄した未接続treeが保持され得る。factoryは受動記録、surface接続は購読という二段階にする。
- UI専用factory群は元の構築表記を分岐させる。genericなComposition adapterで通常のComposeを拡張する。

## 結果と影響

通常のusing static構文でSignalを渡せ、UIが接続・動的な子変更・取り外し・終了に合わせて購読を管理する。状態管理本体の独立性と共有モデルの寿命を維持し、利用者の管理行を減らせる。

UIの依存と接続準備の責務が増える。初期APIは提案段階の拡張で、明示Optional引数等の移行を必要とし得る。awaitをまたぐ暗黙的な依存追跡、子列の自動差分化、暗黙双方向同期は行わない。sourceの差替えは新しい宣言の照合で購読を交換する。生成コードのcompileと実機性能は実装段階で検証する。

## 検証方針

- using static Lumyte.UI.Composeでliteral/Signal/Computed、nullable/default/null、required/init、callback lambda、with、背景slotを実consumer compilationで検証する。
- 未接続treeの変更・放棄とGC、surface接続、動的Children/背景変更、列の並べ替え、取り外し、再接続、Disposeで購読数とモデルからの参照を確認する。
- 接続準備の途中失敗で元のtree/購読を保持し、破棄済みsourceなどの準備エラーを公開前に検出する。別assemblyのcustom Widgetも継承adapterを使えることを確認する。
- dispatcher有無、Idle/Dispatch/Layout/Record、集約、停止済み予約、SignalRevisionとpass中変更、同値更新の抑制・宣言setterの非配送・差分別の表示無効化、外部副作用を検証する。
- 候補準備、quiesce、rollback再提示、commit、GPU退役、Popup/複数Window、IME、既存Automation/Diagnosticsの待機を接続する。
- UI破棄後も共有Signalが使え、任意の業務scopeの解除も値を無効化しないことを確認する。Core単独利用とAOT/Browser/Headlessも検証する。

UI専用の差分反映経路はlayout・入力・描画への影響を分類する。geometry変更はlayoutとElement内入力情報を更新し、入力対象・clip・有効性だけの変更はInvalidateInputで次のUpdateLayoutへ集約する。Fill等の描画だけの変更では入力情報を無効化しない。更新例外では描画・通常入力を停止し、旧状態へのrollbackを行わない。

## 実装記録

2026-10-30、PR #93で独立Signals（#99）へのComposition引数adapter、UiSurfaceの再構築・購読寿命、Window一覧、宣言的Popup、ページ所有の業務scopeを実装した。実装は[UI](../../../src/UI/Lumyte.UI/README.md)、[Windowing](../../../src/UI/Lumyte.UI.Windowing/README.md)、[Application.Navigation](../../../src/UI/Lumyte.UI.Application.Navigation/README.md)を参照する。DialogとAnimationの実装は本PRに含めない。

生成recipeは反復走査で保存・実行し、5,000段のSignalツリーを再構築するテストを追加した。既存の型/key/slot照合、準備中に予約された次の更新、focus・capture・TextBox文書の継続を維持する。構築中の直接Value読出しは構築関数の再実行、Signal引数だけの変更は受動recipeの再実行へ接続する。

宣言的Popupの内容は生成factoryで構築する。手書きWidgetには既存UiPopupRequestを使う。Popupの希望client寸法はopen時に計測し、同sessionの内容更新はその寸法へlayoutする。標準hostはcoordinatorを自動管理し、独自hostはowner layout後のAdvanceとowner解放前のDisposeを担当する。Nativeの実OS動作は各platformの検証対象であり、Linux上の今回の確認はHostedと能力不足fallbackを対象とした。

検証は全solutionのRelease build（warning/error 0）、managed test 39プロジェクト（2,053 passed・75 skipped・0 failed）、独立Signals consoleとHeadless DI pagesの実行を通過した。Signalを直接渡す共有サンプルをBrowserへtrim publishし、Chromium/SwiftShaderで実クリック・新旧GPU画素・同一Window内のsurface交換・URL/history維持・正常終了を確認した。独立SignalsとUI pagesはILLink/Ilc warning-as-errorのlinux-x64 Native AOTでもpublish後の実行を通過した。Markdown lintは246ファイルで0 issues、ADRの相対file linkも検証した。

## 全体レビューに基づく更新境界

受動的な宣言の生成recipeは再利用する。現在の部分木から全体recipeを必要とする場合はElementの採用設定とruntime slotから捕捉し、古いroot宣言の子列を正本にしない。この基本経路では全Surfaceの再構築規則を維持し、重複した走査集合・recipeの確保を省く。全Widgetの部分木更新は本PRで追加し、明示RebuildとSurface構築関数の直接読取にはこの基本経路を使う。

## 再構築候補と購読の所有

予約したWidgetとその準備情報は一つの候補が所有する。表示中の購読はElementに属し、
同じ引数key・型・sourceでは継続する。候補は既存observerを借用し、rendered値の更新を
公開まで保留する。新規observerは候補が所有し、不採用・準備失敗時に解放する。
借用observerを候補の破棄で解除しない。適用中に予約された次の候補も別の所有として管理する。

## 全Widgetの購読境界と部分公開（PR #93）

通常Widgetも関数WidgetもUI-0002のElement更新基盤を使う。
UiArgumentにはsource・値・再生成情報だけを保存し、接続や購読はしない。
購読を持つElementだけがobserverを作り、静的なleafへ空のEffectやUse tableを作らない。
Surface構築関数の直接読取りだけはSurfaceの全体更新境界にする。

通常の引数更新は生成factoryまたはRebuildDeclarationから新しいWidgetを作り、
Element上の設定だけを採用する。未指定の子slotはplan上で保持し、初期化した新しいslotは
その宣言を照合する。旧WidgetChildrenの借用・移管やmounted子Widget配列の修復はしない。
親が子を保持する更新ではdirtyな子も同じbatchで処理し、子を作り直す親にはその更新を集約する。
再生成手段がない場合だけSurfaceの構築関数へ戻る。

Signal通知はElementのdirty集合へ重複なく登録する。無変更時の全境界走査と、
同じbatchの通知ごとの全体再走査を避ける。pause中のrevision、明示更新、再入した
全体Rebuildの優先、HostedのSignalRevision検証は維持する。
任意の関数bodyの直接読取はFunctionElementが保持し、依存集合の変化に応じて更新する。

候補の構築・照合・投影検証・購読変更・PrepareUpdateを公開前に済ませる。
公開ではElementの設定・変更slot・rendered値を確定し、必要なdirtyだけを伝播する。
geometryや入力適格性が同じなら入力情報とversionを再利用し、capture / focus / bookmarkを
新しいWidgetへ付け替えない。Popup・共有key・全体validatorも予定Elementビューで検証する。

宣言生成の部分更新とlayout・入力情報・GPU描画の更新範囲は分ける。
全体validator、兄弟key照合、実際のlayoutや記録に必要な走査は残り、ゼロコストとは扱わない。
後続の#94はproducer lease、#96はpresence・共有key、#97はbody・Use・contextを
同じElementへ接続する。詳細な通知順と検証条件はUI-0002を正本とする。

### 更新集約と購読の所有

変更したElementを深さ順に準備し、祖先の新しい構築結果が既に処理する子を除く。
引数だけを更新して子を保持する親では、dirtyな子も同じbatchへ含める。複数部分木の予定slotを
統合し、重複keyと全体validatorを一回の予定ツリーへ適用してから公開する。
互換性のあるElementでは同じsource / member keyのobserverを継続し、readerだけを採用設定へ更新する。
新規observerは候補が所有し、準備失敗時に破棄する。借用した既存observerは破棄しない。

無変更時は仕事queue・dirty集合・公開revisionを定数時間で確認する。SignalRevisionの確認も
未処理のobserverだけを対象とし、全購読を走査しない。未処理Effectのrevisionはqueueのlock外で
読み、Signal内部lockとの取得順が逆転しないようにする。

Popup内容の計測は一度作成した専用Surfaceで行い、そのSurfaceを表示managerへ移管する。
計測用にmountして破棄し、表示用にもう一度mountする方式を使わない。開く前の取消でも
候補Surfaceを解放する。coordinatorのportal / anchor索引はStructureRevision変更時だけ更新し、寸法・色などの引数変更では再走査しない。

### Element分離後の再検証

ReleaseでUI本体234件、Windowing 47件、編集/IME 39件、Application 33件、
Application.Navigation 25件、Navigation 42件が成功した。dirty親が子を保持する場合の同時更新、
兄弟keyのbatch変更、準備失敗時の借用observer保持、描画中のsource通知、非同期配送、
部分更新後の編集能力検証を含む。DI pages sampleの起動・遷移・終了も成功した。
Popupの計測から表示、開く前の取消を含め、mount / unmountがそれぞれ一回になることを確認した。

UI Widget benchmarkも新しい実体APIへ更新し、100 / 1000部品の中の一つを更新して、
親・兄弟の宣言と変更対象を含むElementの保持を確認した。今回は幅の更新とlayoutを含み、
5 batchの中央値は約99µs / 17,088B、972µs / 130,712Bだった。
この値は本PRの作業ツリーでの測定で、旧Widget実装の測定値や最終stackの再測定とは区別する。

Surfaceの構築関数ごとに外側のSignal読取範囲を遮断する。入れ子のSurfaceの依存は
内側のobserverだけが所有し、破棄済み内側SurfaceのSignal更新で外側を再構築しない。

## Computeでの配列map

keyはinit-onlyの構築値なので、keyへSignal/Computedを直接渡さない。
配列変更はComputeの構築関数でUI部分木へmapする。Computeは独立した読取範囲と再構築recipeを
返る宣言に付け、containerやElementを追加しないためlayoutの意味を変えない。

```csharp
using Lumyte.UI;
using Lumyte.Signals;
using static Lumyte.UI.Compose;

var items = Signal.Create(new[] { "a", "b" });
var view = Panel()[Compute(() => Stack()[
    items.Value.Select(id => Button(key: id, automationName: id)).ToArray()
])];

using var surface = new UiSurface(view);
items.Value = ["b", "c", "a"];
surface.UpdateTree();
```

この例ではComputeが返したStackだけを再構築し、同じ型/keyのButtonElementを保持する。
削除した要素は通常のunmount、追加した要素は新しいmountとなる。配列の同一性をstate identityにしない。

map結果をモデル側のComputedへ置くこともできる。別の変更で親を再構築しても、
同じWidget配列をそのまま新しい親へ渡せる。

```csharp
Computed<Compose.Definitions.Widget[]> rows = Signal.Computed(() =>
    items.Value.Select(id => (Compose.Definitions.Widget)Button(key: id)).ToArray());
var content = Compute(() => Stack()[rows.Value]);
```

Computeは新しい部分木のroot宣言を返す構築関数を受け取る。配列内の宣言は共有できるが、
成功済みWidgetChildrenへの再Initializeや、採用済み宣言のsetter更新には接続しない。
Signalからkeyへの直接bindingは存在しない。新しいidentityが必要な場合はCompute等で新しい宣言を構築し、
その構築値として別のkeyを明示する。子の宣言も新しく構築するため、identity変更で内容を消失しない。

Popup等の再生成用recipeもCompute境界を一つの構築関数として保持し、子slotの復元を重ねない。
再生成後もSignal読取の依存を追跡する。

Compute自体はUseの状態境界を作らない。UseSignal/UseContextは外側の関数componentで取得し、
Computeのdelegateからその値を参照する。Computeが返す子の関数componentは通常どおり独立したUse frameを持つ。

## 同時leaf更新のplan集約

同型・同keyで、旧宣言・新宣言・既存Elementのいずれにも子slotがない連続した更新は、
一つのRebuildPlanへまとめる。親・子を持つ更新、型やkeyを交換する更新は通常の照合経路を使う。
planが一つなら統合用の辞書・slot配列を作らず、そのplanを直接準備する。
全体validator、Signal/animation接続の準備、一括公開、失敗時の清掃は共通経路を維持する。
このため、後続leafの検証が失敗した場合も、先行leafの宣言を公開しない。

関数componentを導入するUI-0023以降は、子slotがない手書きElementでもBuildContentが
初めてcontentを返すことがある。提案した引数・contextで一度だけcontentを準備し、nullなら
leafの集約を続ける。contentがあれば準備結果を通常のplanへ引き渡し、関数を二度呼ばない。
準備中の例外では取得済みanimation leaseを解放する。
