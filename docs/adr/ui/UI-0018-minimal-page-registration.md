# ADR-UI-0018: Minimal API風のページ登録とDI寿命

- 状態: 採用
- 日付: 2026-10-30
- 対応: 実装PR 14、UIアプリケーション拡張PR 3、前提: UI-0016・UI-0017

## 背景

UI専用アプリでは、画面ごとのページとその依存サービスをまとめて登録し、navigationから必要なページだけを生成したい。短い画面は関数で記述し、準備処理や状態を持つ画面はconstructor DIを受けるclassで記述できると、アプリの構成を一箇所で確認できる。

既存のCompositionはWidgetの構築構文を生成する。Composableには引数なしconstructor等の制約があり、DIページの生成、非同期準備、画面の寿命は担当しない。また標準Microsoft DIのscopeはWindowごとの階層的な継承を提供せず、非同期Disposeを開始した後の再試行やUI ownerへの復帰も保証しない。

## 決定

### 配置と責務

UI-0017で導入した.NET 10の任意統合package `src/UI/Lumyte.UI.Application.Navigation`を拡張する。namespaceとNuGet名も同名とし、UIアプリケーションのbuilder、navigation、Microsoft.Extensions.DependencyInjectionを接続する。UI本体、Composition、navigation coreへDIを逆流させない。

UiApplicationBuilderのServicesとBuild、hostの起動・終了は[UI-0016](UI-0016-ui-application-framework.md)、型付きdestinationと遷移の準備・公開・退役は[UI-0017](UI-0017-navigation-and-page-lifetime.md)を正本とする。本ADRはそのactivation providerを実装し、MapPage/MapGroupとページscopeを定義する。ルーティングのHTTP解釈、assembly走査、任意Delegateの引数推論、別のDI containerは追加しない。

### 実装との対応

既存の任意package `Lumyte.UI.Application.Navigation`へ`IUiPage`、`UiPageContext`、`MapPage`／`MapGroup`、登録型の`UseNavigation`、`UiPageWindows.Page`を追加する。Application本体に残す変更は内部のBuild検証hookだけで、Navigation／DIページ型への参照を追加しない。

登録tableをBuildで検証・コピーし、標準hostのWindow contentから軽量なactivationを生成する。ScopeをPrepare開始直後に所有欄へ保存し、contextを初期化してからDIを解決する。途中失敗も既存navigationの候補退役経路に残し、Retire成功後・Surface解放後に標準DIの非同期破棄を一度だけ開始する。

回帰試験は`Lumyte.UI.Application.Navigation.Tests`、実行例は`Lumyte.UI.Pages.Sample`／`Lumyte.UI.Pages.Browser.Sample`へ置く。関数ページ・constructor DI・型付き引数・scope清掃を同じsampleで実行する。独自providerを渡す従来のUseNavigationと、基本Application／driver／DiagnosticsのNavigation非依存を維持する。

### 公開API

比較元はUI-0017を実装したPR #90（3427b2e9aaa665fc5f375c56332d08273f9294ad）。Widgetは既存の`Lumyte.UI.Compose.Definitions.Widget`、UiPageKey、UiNoArgs、UiPageActivationContext、IUiPageLeaveGuardはUI-0017の型を使う。

```diff
+namespace Lumyte.UI.Application.Navigation
+{
+    public sealed class UiPageContext
+    {
+        // activationごとの借用view。DI解決前に一度確定し、他ページへ再利用しない。
+        // constructorは公開しない。scopeのIServiceProviderを公開しない。
+        public UiPageActivationContext Activation { get; }
+        // UseTextInputで選んだWindowContextのproviderを借用。未設定はnull。
+        public Lumyte.UI.TextEditing.IUiTextLayoutProvider? TextLayoutProvider { get; }
+    }
+    public interface IUiPage
+    {
+        // constructorの後・Buildの前に一回。既定は完了済み。
+        // I/O・データ準備専用。await後のUI ownerを仮定しない。
+        ValueTask PrepareAsync(UiPageContext context, CancellationToken cancellationToken = default);
+        // UI ownerのIdleで一回。Compositionで未接続rootを作り返す。
+        Widget Build(UiPageContext context);
+        // 既定null。UI-0017の離脱判定へ借用転送し、DIで別instanceを作らない。
+        IUiPageLeaveGuard? LeaveGuard { get; }
+        // 未公開・準備失敗でも呼ぶ。取消・発火なしで所有物を返す。
+        // 既定は完了済み。成功済み工程を重ねず、失敗後は同じinstanceで再試行可能。
+        ValueTask RetireAsync(CancellationToken cancellationToken = default);
+    }
+    public static class UiPageRegistrationExtensions
+    {
+        // no-args pageをScoped登録し、完全なkeyを返す。登録中にpageは生成しない。
+        public static UiPageKey<UiNoArgs> MapPage<
+            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage>(
+            this UiApplicationBuilder builder, string key) where TPage : class, IUiPage;
+        // captureArgsはUI-0017の不変destination作成時に使う。nullは拒否。
+        public static UiPageKey<TArgs> MapPage<
+            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TArgs>(
+            this UiApplicationBuilder builder, UiPageKey<TArgs> key, Func<TArgs, TArgs> captureArgs)
+            where TPage : class, IUiPage;
+        // 固定signatureの同期handler。サービスは当該page scopeから型付きで解決する。
+        public static UiPageKey<UiNoArgs> MapPage(this UiApplicationBuilder builder, string key,
+            Func<UiPageContext, Widget> handler);
+        public static UiPageKey<UiNoArgs> MapPage<T1>(this UiApplicationBuilder builder, string key,
+            Func<UiPageContext, T1, Widget> handler) where T1 : notnull;
+        public static UiPageKey<UiNoArgs> MapPage<T1, T2>(this UiApplicationBuilder builder, string key,
+            Func<UiPageContext, T1, T2, Widget> handler)
+            where T1 : notnull where T2 : notnull;
+        public static UiPageKey<UiNoArgs> MapPage<T1, T2, T3>(this UiApplicationBuilder builder, string key,
+            Func<UiPageContext, T1, T2, T3, Widget> handler)
+            where T1 : notnull where T2 : notnull where T3 : notnull;
+        // prefixの束を返す。DI scope/providerやWindowは作らない。
+        public static UiPageGroupBuilder MapGroup(this UiApplicationBuilder builder, string prefix);
+        // 初期destinationと標準DI activation providerを登録する。初回生成はRun時。
+        public static UiApplicationBuilder UseNavigation(this UiApplicationBuilder builder, string initialKey,
+            UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null);
+    }
+    public sealed class UiPageGroupBuilder
+    {
+        // 内部constructor。所属builderとprefixは作成後不変。
+        public string Prefix { get; }
+        public UiPageGroupBuilder MapGroup(string prefix);
+        public UiPageKey<UiNoArgs> MapPage<
+            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage>(
+            string localKey) where TPage : class, IUiPage;
+        // prefixを結合したkeyを新規に作って返す。
+        public UiPageKey<TArgs> MapPage<
+            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TArgs>(
+            string localKey, Func<TArgs, TArgs> captureArgs) where TPage : class, IUiPage;
+        public UiPageKey<UiNoArgs> MapPage(string localKey,
+            Func<UiPageContext, Widget> handler);
+        public UiPageKey<UiNoArgs> MapPage<T1>(string localKey,
+            Func<UiPageContext, T1, Widget> handler) where T1 : notnull;
+        public UiPageKey<UiNoArgs> MapPage<T1, T2>(string localKey,
+            Func<UiPageContext, T1, T2, Widget> handler)
+            where T1 : notnull where T2 : notnull;
+        public UiPageKey<UiNoArgs> MapPage<T1, T2, T3>(string localKey,
+            Func<UiPageContext, T1, T2, T3, Widget> handler)
+            where T1 : notnull where T2 : notnull where T3 : notnull;
+    }
+}
```

IUiPageの既定PrepareAsync/RetireAsyncは完了済みValueTask、LeaveGuardはnullを返すdefault interface実装とする。Buildは必須とし、UiSurfaceの生成・登録・破棄をページへ委譲しない。UiPageContextはUI-0017のactivation情報を渡すだけで、別のnavigation状態やWindow lifetimeを持たない。引数は`Activation.Destination.GetArguments<TArgs>()`からコピーを取得し、履歴の正本snapshotへの可変参照を渡さない。

### 登録・group・凍結

MapPageはimmutableなkey・引数型・capture処理・型付き生成処理を一つの登録として保持する。登録時にはconstructor、PrepareAsync、Build、handler、native初期化を呼ばない。MapGroupはprefixを結合するだけで、groupごとにServicesやproviderを作らない。group内の`/general`はgroup rootからのlocal keyとし、`/settings`のgroupへ登録した結果は`/settings/general`となる。local keyが`/`ならprefix自身、prefixが`/`ならlocal key自身を完全keyとし、余分な末尾`/`を作らない。入れ子でも同じ規則を使い、戻ったkeyがnavigationの正本になる。

keyの文字・長さ・比較規則はUI-0017へ揃える。`/`または小文字ASCII segmentを`/`で結合したkeyを使い、prefix結合後に検証する。同じ完全keyの再登録は引数型が異なってもArgumentExceptionとする。wildcard、route parameterの暗黙bind、queryからDI引数への変換は行わない。null handler/captureはArgumentNullException、不正なkey/prefixはArgumentExceptionとし、失敗した一件の登録前の状態を維持する。

typed登録のcaptureArgsはUI-0017のCaptureとGetArgumentsに使い、純粋・決定的なdeep copyを要求する。可変な引数をそのまま返す関数は契約違反である。取得済みのhistory snapshotを登録コードやページの変更で書き換えず、登録tableにないroute・引数型のdestinationを受け入れない。

`MapPage<TPage>`はTPageを一件だけunkeyed Scoped登録する。同型を複数keyへ登録してもservice登録は増やさない。既存TPage登録を使う場合もunkeyed Scoped一件を要求し、Singleton、Transient、instance、重複登録を拒否する。IUiPageへのaliasや`IEnumerable<IUiPage>`の一括解決は作らず、選択されたTPageだけを解決する。ページのconstructor factoryを明示する場合は、MapPageの前に通常の`AddScoped<TPage>(factory)`を使える。

明示factoryもscopeごとに別のpage instanceを作り、外部所有・キャッシュ済みのpageを返さない。アプリが独自のDisposable aliasを追加すると同じinstanceがDIへ重複追跡され得るため、TPageへの別service型の所有aliasは作らない。任意factoryの内部を登録時に実行・解析して検証することはしない。

Buildはその時点のServicesと登録一覧を検証・コピーして凍結し、groupを含む後続登録をInvalidOperationExceptionで拒否する。Buildを登録containerの追加入口として再利用しない。UseNavigationは一回だけ設定でき、初期keyがUiNoArgsとして解決可能かもBuildで検証する。初期画面を登録順から選ばない。factory内部の任意コードをBuildで実行して検証せず、その失敗はUI-0017の準備失敗として扱う。

UseNavigationのoptionsはコピーしてUI-0017の検証を適用し、onCompletedはUiNavigatorへそのまま渡す。callbackはアプリ内容と同じ寿命であり、ページscopeのinstanceを捕捉しない。省略時もLastResultは保持され、独自の結果通知eventを追加しない。

UseNavigationはUI-0016のUseContentへnavigation用IUiApplicationContent factoryを一つ登録する。UseRootまたは他のUseContentとの併用は同じcontent slotの二重指定として拒否する。内部のDI activation providerはrootのIServiceScopeFactoryと凍結した登録tableを借用し、登録済みkeyと引数型を確認してからUI-0017のUiPageDestination.Captureを呼ぶ。Createは同じcontextの軽量IUiPageActivationを返すだけで、DI scopeやserviceを生成しない。navigationがそのactivationの所有を受け取ってからPrepareAsyncを呼ぶ。registryをDIから解決するために別providerをBuildしない。

content factoryは内部NavigationContentをnewしてhostへ所有を移す。content自体をDI serviceとして登録・解決せず、hostとproviderの二重所有を作らない。借用したroot serviceからnavigationとactivation providerを構成し、contentがnavigationと各page activationを所有する。

contentはRunのowner確立後に生成する。InitializeAsyncではUiApplicationWindowContext.Viewに対応するUI-0017のnavigation hostを作り、初期RequestNavigateのCompletionがCommittedとなるまで進めてから戻る。他の結果は初期化失敗として清掃し、root WindowをShowしない。初回成功後にroot WindowをShowする。Advance/BeginStop/IsStoppedはnavigationの更新・新規要求停止・操作停止へ接続する。

終了時はslotのGPU退役後もsurfaceを保持し、content.RetireAsyncで現在のactivationの再試行可能な清掃を進める。成功後にslotがsurfaceを解放し、ReleaseAsyncで残るpage scopeのDisposeを一度だけ行う。page遷移ごとにNative Windowやroot登録を作り直さない。

### ページscopeとComposition

ページとアプリサービスのDI rootはアプリごとに一つとする。UI-0016のprivate render providerはその登録表を複製せず、ページ解決に使わない。各IUiPageActivationはPrepareAsyncの開始時にrootのIServiceScopeFactoryから独立したAsyncServiceScopeを作る。scopeは取得直後にactivationの所有欄へ保存し、UiPageContextの一回限りのbindingを初期化してから、同じscopeのTPageまたはinline handlerのT1〜T3を解決する。未初期化contextの解決とroot providerからのページ解決は拒否し、ValidateScopes/ValidateOnBuildを有効にする。

UiPageContextと内部binding holderはframeworkが一件のScoped登録として用意し、アプリによる置換・重複をBuildで拒否する。binding holderは当該scopeだけに属し、rootや別activationに値を保存しない。初期化はDI factoryからページを解決するより先にowner上で行い、以後は不変とする。activationのWindowに一致するUiApplicationWindowContextからTextLayoutProviderも借用して固定する。providerを所有するDI aliasは追加せず、Navigation coreへTextEditing依存を増やさない。

Microsoft DIのscopeは親scopeのScoped instanceを継承しない。Windowの下にscopeを作れば同じWindow serviceを得られるとは扱わず、Window/navigation/dispatcherはactivation contextの借用参照として渡す。Window所有のDisposableをpage scopeのfactoryの戻り値にして、page終了時に破棄させない。ResourceManagerとExecutionRuntimeは既存契約どおりroot singletonとし、UIアプリのgraphics deviceもアプリ所有とする。既定ScopedのAddGraphicsDeviceをページごとのdevice生成として使わない。

TPage自体はComposableにしない。通常classのconstructor DIで依存を受け取り、PrepareAsyncでデータを準備し、UI ownerのIdleでBuildを呼んで既存Compose.*から新しいrootを作る。PrepareAsyncのawait継続からWidgetやWindowを操作せず、戻った後にhostのowner配送を通してBuildする。Buildは同期CPU処理とし、I/O、同期wait、pump再入を行わない。外部から見えるページへの接続はUI-0017の公開境界だけで行う。

inline handlerもactivationごとに一回、同じownerのIdleで呼ぶ。0〜3個のサービスは同じpage scopeから`GetRequiredService<T>`で解決し、引数の位置と型を変換しない。戻り値は新しい未接続rootでなければならず、null・別surfaceへの接続・共有rootは準備失敗とする。callbackは生成時のパラメータへ渡し、captured serviceやcontextの寿命をそのページに限定する。handlerはrender/frameごとには呼ばない。

inlineは同期構築のための入口であり、非同期準備・再試行可能な退役を必要とするページはIUiPageを使う。注入serviceは最後のDI Disposeをthread-neutralにできるものに限る。constructor/factoryではowner資源を起動せず、pageのPrepare/Retireまたはアプリhostの明示lifecycleで管理する。

TextBoxはCompositionの`layoutProvider: context.TextLayoutProvider`へ明示して渡す。未設定または編集capability不足の扱いはUI-0016のview準備時検証に従い、ページごとに別の既定font/providerを生成しない。

### 退役・失敗・取消

生成されたadapterはUI-0017のIUiPageActivationとして、Root、借用LeaveGuard、ページとscopeを一つのactivationへ結び付ける。Prepare/Buildに失敗してもnavigationが既に所有するactivationへ途中のscopeを残し、既存ページを公開したまま候補側を退役する。IsPreparedがtrueになる前のRoot参照は拒否し、同じactivationでPrepareを再実行しない。frameworkからのcallback・非同期完了は取消tokenとactivation世代で拒否し、アプリが別に保持したcallbackを退役後に直接呼ぶことは認めない。履歴はdestinationとコピー済み引数だけを保持し、Back/Forwardでも新しいscopeとtreeを作る。

UI-0017の入力・Popup・IME・Diagnostics操作の停止と、[UI-0008のGPU退役](UI-0008-render-graph-and-resource-lifetime.md#所有権失敗)を先に完了する。その後IUiPage.RetireAsyncが、ページとその依存に残るowner資源・Reference・購読・保持しているcallbackを返す。未公開・Prepare途中失敗でも呼ばれるため、未取得の資源を解放済みと誤認せず、部分成功をinstanceへ記録する。必要なUI操作はownerへ配送し、await後のthreadを仮定しない。

通常遷移と失敗候補のRetireは、UI-0017のview changeが提供するRunPageCleanupAsyncでhostのcleanup admissionを受けて実行する。失敗候補でもview changeはpageのPrepare前から取得済みなので、scope生成後・Build前の失敗にも同じ経路を使う。app停止中の現在pageはUI-0016のcontent.RetireAsyncの追跡範囲を使う。これによりConfigureAwait(false)後のDispatcher呼出も清掃として受理され、通常workの停止拒否と衝突しない。清掃Taskを全てawaitし、追跡範囲から未完了workを逃がさない。

失敗候補はview changeのDrainCandidateAsyncで候補のprepared/GPU使用を終え、同じactivationのRetireAsync、DiscardAsyncによる候補surface解放と旧gate復帰、activation.DisposeAsyncの順で閉じる。候補のlayout失敗やguard拒否でも、surfaceだけを先に解放してpageのowner専用清掃を実行不能にしない。

退役で返すのは当該activationが所有する資源だけである。借用したsingleton service、ResourceManager、Window manager、navigation、dispatcherは破棄しない。ページのDI constructor自体が失敗してTPageを得られない場合も、作成済みscopeをDisposeできるよう、constructor/factoryによるowner資源の取得を禁止する。owner資源の取得は、TPageをactivationへ保存した後のPrepareから始める。

RetireAsyncの失敗ではscopeと必要なhostを維持して同じactivationで再試行する。退役成功後にUI-0017のhostが旧surfaceを解放し、その後にAsyncServiceScope.DisposeAsyncを一度だけ開始する。標準DIの非同期破棄は後続serviceをUI owner上で呼ぶ保証がないため、ページだけでなく依存serviceのowner専用清掃もここより前に終える。ページinstanceをadapterが別途DisposeしてからDIへ再度渡すことはしない。

scopeのDisposeAsync自体が失敗した場合は、RetireAsyncとは異なる終端cleanup failureとして保存・報告する。標準DIではdispose済みフラグが先に立つため、同じscopeを呼び直して残余serviceの清掃が再開すると保証しない。adapterの再呼出でも保存した失敗を成功へ変えず、同じscopeを新ページへ再利用しない。アプリ終了の扱いはUI-0016のfault/終了報告へ接続する。

### AOTと利用例

MapPageの型引数には標準DIと同じPublicConstructors保持注釈を付ける。inlineは閉じたgenericの直接呼出にし、Delegate.DynamicInvoke、Expression.Compile、名前からの型ロードを使わない。Compositionが生成した独自factory delegateは`static context => Compose.*(...)`で包み、任意Delegateとして解析しない。新しいGeneratorは不要で、serviceの明示factory登録も使える。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.Navigation;
using Lumyte.Graphics.Drawing2D;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Compose;

var builder = UiApplication.CreateBuilder(args);
builder.UseDesktop();
builder.Services.AddScoped<PreferencesModel>();
var settings = builder.MapGroup("/settings");
var general = settings.MapPage<GeneralPage>("/general");
settings.MapPage<PreferencesModel>("/quick", static (context, model) =>
    Panel()[Button(onClick: model.Toggle, width: 160f, height: 40f)
        [Button.Background()[Rectangle(fill: new Color2D(0.2f, 0.4f, 0.8f, 1f))]]]);
builder.MapPage("/", context =>
    Panel()[Button(width: 160f, height: 40f, onClick: () =>
    {
        context.Activation.Navigation.RequestNavigate(general, default);
    })[Button.Background()[Rectangle(fill: new Color2D(0.2f, 0.4f, 0.8f, 1f))]]]);
builder.UseNavigation("/", onCompleted: result => Console.WriteLine(result.Status));
await using var app = builder.Build();
await app.RunAsync();
```

登録はアプリ構成時だけに行い、取得したkeyはnavigationへ渡す。例のdefaultはgeneralの型からUiNoArgsになる。RequestNavigateはcallbackから要求をqueueするだけで、遷移結果はUI-0017のticket.Completion、LastResult、onCompletedで観測する。callbackをasync voidにせず、UI処理を同期waitしない。

classページとinlineの差は構築の表現だけで、scope、入力配送、UIのComposition、Automation/Diagnostics、終了順は同じ契約を使う。Native AOT、Browser publish、trimmingはこの組合せで検証し、既存Generatorの対応だけを新しい統合の検証済み実績としない。

### Window宣言からページを指定する

2026-10-29追記。MapPageはページ登録だけを行い、表示Windowの生成とは分ける。複数Windowは[UI-0016](UI-0016-ui-application-framework.md#複数windowの宣言api)のUseWindowsで宣言し、各Windowのcontentへ初期ページ用factoryを渡す。Application coreへNavigation/DIページの型を逆参照させないため、`Window(page: ...)`というcore overloadは追加せず、統合packageの静的Page関数を使う。

```csharp
namespace Lumyte.UI.Application.Navigation;

public static class UiPageWindows
{
    public static Func<IServiceProvider, IUiApplicationContent> Page(
        UiPageKey<UiNoArgs> page, UiNavigationOptions? options = null,
        Action<UiNavigationResult>? onCompleted = null);
    public static Func<IServiceProvider, IUiApplicationContent> Page<TArgs>(
        UiPageKey<TArgs> page, TArgs arguments, UiNavigationOptions? options = null,
        Action<UiNavigationResult>? onCompleted = null);
}
```

```csharp
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Application.Navigation.UiPageWindows;

var editor = builder.MapPage<EditorPage>("/editor");
var inspector = builder.MapPage<InspectorPage>("/inspector");
builder.UseWindows(app => [
    Window(key: "main", title: "エディター", content: Page(editor),
        onCloseRequested: _ => app.RequestStop()),
    Window(key: "inspector", title: "プロパティ", owner: "main",
        isOpen: showInspector, content: Page(inspector),
        onCloseRequested: _ => { showInspector = false; app.RequestRebuildWindows(); })
]);
```

Pageは型付きの初期destination指定を保持したfactoryを返し、この時点でproviderをresolveしたりscope/pageを生成したりしない。Window instance開始時のfactory実行で、同じbuilderの凍結tableとroot IServiceScopeFactoryから専用NavigationContentを生成する。未登録page/引数型違いは初回表示準備の失敗とし、そのWindowをShowしない。引数はinstance開始時に登録copierでcaptureし、準備後のモデル変更と分離する。Page呼出し時から開始までの可変引数の競合は利用者が避け、同じ宣言の継続中に引数を書き換えて再navigationする用途には使わない。

同key再構築で新しいPage factoryが返っても既存content/navigationを保持し、初期destination/argsを再適用しない。別ページへの移動は当該Windowのnavigatorへ要求する。Windowの再追加/置換では新しいnavigatorとpage scopeを作る。複数Windowの同型TPageもactivationごとの別Scoped instanceとして解決し、Window context/navigationをSingletonへ保持しない。

UseNavigationはprimary一つの簡易形として従来どおりUseContentへ展開し、UseWindowsとの併用は内容の二重指定として拒否する。MapPage/MapGroupだけの登録はUseWindowsと併用可能で、Windowごとに再登録を要求しない。受け入れ条件へ同じ登録pageの二Window同時表示、別scope/context、片側の終了だけで片側scopeを解放、同key再構築でconstructor/Prepare/Buildが再実行されないことを加える。ページ内Signal再構築は別途UI-0019の通常規則に従う。

## 検討した代替案

- Build後のapp.MapPageでDI登録も追加すると、完成済みproviderとservice一覧が食い違う。builder段階で登録を完了させる。
- ページ自体をComposableにするとconstructor DIと既存Generatorの構築規則が衝突する。ページの中でCompositionを使う。
- 任意Delegateの引数をReflectionで調べるとAOTの保持とbinding規則が別途必要になる。class登録と固定0〜3引数のtyped overloadを使う。
- groupごとにproviderを作るとsingletonと終了順が分裂する。groupはkeyを整理し、scopeはactivationごとに作る。
- rootや履歴にページinstanceを保持すると、異なるWindowや再遷移で同じtreeとscoped stateを共有し得る。初期版は毎回新しいscopeを作る。
- DI Disposeだけで退役するとGPU、IME、操作Jobの完了待ちと再試行を保証できない。明示Retireの後にscopeを閉じる。

## 結果と影響

ページの一覧、DI依存、初期画面をbuilderへ集め、Compositionによる保持型UIをクラス・関数の両方から構築できる。既存のDI、navigation、GPU・Resources寿命を使い、UI本体の依存境界を維持する。

一方、ページごとにscopeとtreeを作るため、Back/Forwardでinstance状態は保持しない。inlineのDI引数は3個までとし、多い依存や非同期処理にはclassページを使う。任意のservice破棄失敗を自動復旧することは保証しない。

## 検証方針

- class、inline0〜3引数、入れ子groupが同じ完全key規則を使い、重複key・型違い・不正prefix・Build後変更を変更前に拒否する。UseNavigationの未登録初期keyと二重設定を検証する。
- constructor/handler/Prepareを登録・Build中に呼ばず、各activationで一回だけ呼ぶ。事前登録Scoped factory、同型の複数key、Singleton/Transient/重複拒否、IUiPage aliasなしを確認する。
- 現在pageと候補page、Back/Forwardでページscopeが分かれ、root singletonが共有される。context初期化前・rootからのpage解決を拒否し、Window所有物をページ終了で破棄しない。
- PrepareのI/O完了threadによらずBuildと接続がowner Idleに戻り、未接続root・callbackパラメータ・constructor DIと既存Composition Generatorが動く。
- constructor・DI factory・Prepare・Buildの各失敗と取消で旧画面を保ち、候補scopeを失わない。async処理の遅い完了で退役済みページを再公開しない。
- Popup/IME/Diagnostics/GPUの清掃後にRetire、scope Disposeの順で進む。Retire失敗は同じscopeで再試行し、scope Dispose失敗は終端エラーとして保存する。二重Disposeや誤ったowner threadでの清掃を防ぐ。
- Native AOTとBrowserのtrim publishでclass/inlineの画面生成・遷移・終了を実行する。Reflection探索やdynamic-code warningを抑制して通さず、公開型の保持注釈と明示factory経路を確認する。

## 検証記録

Linux、.NET SDK 10.0.401で確認する。NativeのWindow／STA／TSF、実機GPUのdevice loss、Browserの実IME操作をこの検証の実績には含めない。

| 検証 | 結果と範囲 |
| --- | --- |
| Release solution build | 警告0・エラー0。登録API、2 sample、新規test projectを含む |
| managed試験 | 全38 projectで2,024件成功、75件skip、失敗0。ページ統合23件、既存Navigation42件・Application24件を含む |
| 登録・scope | classとinline 0〜3 service、group、遅延生成、登録凍結、context上書き拒否、独立scope、root／未初期化scopeからの解決拒否を確認 |
| 失敗・寿命 | factory・constructor・Prepare・Build・null root・取消・guard拒否で旧画面を保持。退役再試行、停止後のowner清掃、scope破棄一回と終端失敗を確認 |
| 複数Window | 型付き引数の独立コピー、別context／navigator／page scope、同key再構築で再生成しないこと、片側の閉鎖・再追加を確認 |
| Headless | 実行sampleで関数ページからclassページへ遷移し、同じWindowでSurfaceを交換、Scope2件を各一回解放 |
| Browser | trim publish、Chromium／SwiftShader WebGPU、CPU倍率6で通常クリックと旧新GPU PNGを検証。WindowとURL/historyを維持し、Surfaceと描画色を交換、終了時のscope数も検査 |
| Native AOT | strict publishと生成実行ファイルでclass／関数の生成、constructor DI、型付きcontext、遷移、scopeの各一回破棄を確認。警告の抑制なし |
| package・依存 | Application.NavigationのpackとDLL／README／依存関係を検査。基本Application・3 driver・基本DiagnosticsのNavigation非依存を再確認。後続を含むMarkdown 246ファイルでlint問題0件 |

初回の全体試験は全アサーション成功後に、既存Windows TextInput testのVisual Studio test adapterのPortableSymbolReader.Disposeから終了時例外が出た。該当8件と全38 projectの逐次再実行はexit 0で完了した。今回の変更にPlatform実装やtest runnerの変更は含めない。

## 参考資料

- [Compositionの構築規則](../composition/COMPOSITION-0001-declarative-composition.md)
- [UIアプリケーションhost](UI-0016-ui-application-framework.md)
- [Navigationとページ寿命](UI-0017-navigation-and-page-lifetime.md)
- [実行基盤のDI・終了](../core/CORE-0003-execution-and-jobs.md#diありの構成例)
- [Resourcesの実行器・DI](../resources/RESOURCES-0003-runtime-execution.md)
- [.NET 10のDI scope実装](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ServiceProviderEngineScope.cs)
