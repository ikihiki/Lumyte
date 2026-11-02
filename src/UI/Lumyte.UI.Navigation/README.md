# Lumyte.UI.Navigation

DIやPlatformから独立した、Windowごとのページ切替と履歴です。型付きの行き先、Push／Replace／Back／Forward、非同期準備、離脱guard、取消、清掃の再試行を扱います。

標準アプリでは任意の`Lumyte.UI.Application.Navigation` packageの`UseNavigation`、または各Windowのcontent factoryから`UiNavigationApplicationContent`を使います。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.UseNavigation(pages, Home, default(UiNoArgs));
await using var app = builder.Build();
await app.RunAsync();
```

`pages`は`IUiPageActivationProvider`です。実行できる実装例は[Navigation sample](../../../samples/Lumyte.UI.Navigation.Sample/ExamplePages.cs)を参照してください。DIでのページ登録は任意の[Application.Navigation](../Lumyte.UI.Application.Navigation/README.md)の`MapPage`／`MapGroup`で利用できます。Buttonのcallbackからは、ページのactivation contextを使って要求だけを渡します。

```csharp
using static Lumyte.UI.Compose;

Button(onClick: () => context.Navigation.RequestNavigate(Details, new DetailArguments(42)));
```

要求は次のowner更新で進み、callback中にページを差し替えません。`Completion`は`Committed`、`Rejected`、`Canceled`、`Failed`、`Busy`、`NoHistory`の結果を返します。ページの退役が残っている間、次の要求は`Busy`です。`onCompleted`は結果の確定後にownerで呼ばれます。

routeは`/`または小文字ASCIIの静的segmentです。URL decodeやplaceholder展開は行いません。`UiPageKey<TArgs>`は登録型と完全一致させます。providerは登録済みcopierで引数をcaptureし、`GetArguments<TArgs>`もコピーを返します。mutableな引数には独立したコピーを作る関数を用意してください。

履歴の既定上限は64です。履歴は行き先と引数だけを保持し、Back／Forwardでも新しいActivationIdとUiSurfaceを作ります。formやscrollの復元に必要な状態はアプリのmodelへ保存します。

候補は非公開で準備し、guard承認後に旧入力・Popup・IME・診断操作をdrainして公開します。旧ページはGPU退役→pageの`RetireAsync`→surface解放→leaseの`DisposeAsync`の順で清掃します。失敗した清掃はsnapshotの`RetirementFailure`で確認できます。`RetryRetirementAsync`の開始だけをdispatcherへ渡し、そのTaskを外で待機してください。最終`DisposeAsync`の失敗は再実行しません。

独自hostには`IUiNavigationHost`／`IUiNavigationViewChange`を実装できます。通常のアプリでは標準hostが`Advance`、入力gate、停止と清掃を管理します。BrowserのURL/history同期、KeepAlive、nested outlet、遷移animationは初期範囲に含みません。

Navigation専用の診断metadataは[Application.Navigation.Diagnostics](../Lumyte.UI.Application.Navigation.Diagnostics/README.md)の`UseNavigationDiagnostics`で追加します。基本DiagnosticsはNavigationを参照しません。

詳細は[ADR UI-0017](../../../docs/adr/ui/UI-0017-navigation-and-page-lifetime.md)を参照してください。
