# Lumyte.UI.Application.Navigation

標準ApplicationとNavigationを接続する任意のpackageです。`Lumyte.UI.Application`とDesktop／Browser／Headless driverはNavigationを参照しません。このpackageがApplicationと`Lumyte.UI.Navigation`へ依存します。

```csharp
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Navigation;

builder.UseNavigation(pages, Home, default(UiNoArgs));
```

`pages`、`Home`、`UiNoArgs`の定義は[Navigation](../Lumyte.UI.Navigation/README.md)を参照してください。複数Windowでは各content factoryが新しい`UiNavigationApplicationContent`を返します。同じWindow instanceではnavigatorと履歴を保持します。

`UiApplicationViewSlot`の`CreateNavigationHost`と`CaptureNavigationSnapshot`は、このpackageの拡張メソッドです。表示交換・入力・IME・GPU退役は標準host、行き先と履歴の管理はNavigationが担当します。`Navigation`と`WindowContext`は借用参照です。

Navigation専用の診断metadataは、さらに任意の[Application.Navigation.Diagnostics](../Lumyte.UI.Application.Navigation.Diagnostics/README.md)へ分離しています。
