# Lumyte.UI.Application.Navigation

標準ApplicationとNavigationを接続する任意のpackageです。`Lumyte.UI.Application`とDesktop／Browser／Headless driverはNavigationを参照しません。このpackageがApplication、`Lumyte.UI.Navigation`、Microsoft DIを接続します。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Desktop;
using Lumyte.UI.Application.Navigation;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Compose;

var builder = UiApplication.CreateBuilder(args).UseDesktop();
builder.Services.AddScoped<PreferencesModel>();
var settings = builder.MapGroup("/settings");
var general = settings.MapPage<GeneralPage>("/general");
builder.MapPage("/", context =>
    Button(width: 160, height: 40,
        onClick: () => context.Activation.Navigation.RequestNavigate(general, default)));
builder.UseNavigation("/");
await using var app = builder.Build();
await app.RunAsync();
```

`GeneralPage`は通常のclassとして`IUiPage`を実装します。constructorでScoped serviceと`UiPageContext`を受け取り、任意の`PrepareAsync`でデータを準備し、必須の`Build`で未接続rootを返します。`Build`はUI ownerで呼びます。`RetireAsync`でowner資源を返してから、frameworkがDI scopeを一度だけ破棄します。ページ自身をComposableにする必要はありません。

短いページは`MapPage<T1, T2, T3>(key, (context, service1, service2, service3) => root)`のように0〜3個のserviceを受ける同期handlerで書けます。同じactivationのサービスは一つのscopeから解決し、handlerはそのactivationの初回構築で一回呼びます。非同期準備や再試行可能な清掃にはclassページを使います。

`MapPage<TPage, TArgs>(typedKey, copyArguments)`とgroupのtyped overloadで型付き引数を登録できます。コピーは純粋で独立したsnapshotを返す関数とし、ページでは`context.Activation.Destination.GetArguments<TArgs>()`でコピーを取得します。groupはprefixを結合するだけでDI scopeを作りません。

登録とservice一覧は`Build`で検証・固定します。重複route、同じpage型のSingleton／Transient／重複登録、contextの上書き、未登録の初期ページを拒否します。事前の`AddScoped<TPage>(factory)`を使用でき、登録・Build中にfactoryを実行しません。pageはrootや未初期化の独立scopeから解決できません。

複数Windowでは`using static Lumyte.UI.Application.Navigation.UiPageWindows;`を追加し、`Window(..., content: Page(pageKey))`または`Page(pageKey, arguments)`を使用します。各Windowは新しいnavigatorとpage scopeを持ち、同じWindow keyの継続宣言では現在ページを保持します。`UseNavigation`はprimary一つの簡易形なので`UseWindows`と同時には設定しません。

`UiPageContext.TextLayoutProvider`はWindowのproviderを借用します。TextBoxには`layoutProvider: context.TextLayoutProvider`として明示します。履歴は行き先と引数だけを保持し、Back／Forwardでもpageとscopeを作り直すので、復元する状態はアプリのmodelに保存してください。

独自の`IUiPageActivationProvider`を持つ場合は従来の`UseNavigation(provider, initialKey, arguments)`も利用できます。`UiNavigationApplicationContent`、slotの`CreateNavigationHost`と`CaptureNavigationSnapshot`もこのpackageにあります。標準hostが表示交換・入力・IME・GPU退役を自動管理します。

実行できる[Headless sample](../../../samples/Lumyte.UI.Pages.Sample/README.md)と[Browser sample](../../../samples/Lumyte.UI.Pages.Browser.Sample/README.md)、[ADR UI-0018](../../../docs/adr/ui/UI-0018-minimal-page-registration.md)を参照してください。Navigation専用の診断metadataは任意の[Application.Navigation.Diagnostics](../Lumyte.UI.Application.Navigation.Diagnostics/README.md)へ分離しています。
