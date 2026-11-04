# Browser navigation sample

`dotnet publish samples/Lumyte.UI.Navigation.Browser.Sample -c Release -o artifacts/browser/ui-navigation`で出力し、`wwwroot`をHTTP配信してください。標準hostがBrowserの描画・入力・ページ切替を管理します。

Chromiumを用意し、`node samples/Lumyte.UI.Navigation.Browser.Sample/verify-browser.mjs artifacts/browser/ui-navigation/wwwroot`で検証できます。通常クリックでページを切り替え、GPU PNGの画素、Window IDの維持、Surface IDの交換、URL/historyが変わらないこと、終了を検査します。

標準hostとの接続には任意の`Lumyte.UI.Application.Navigation`を明示参照し、`using Lumyte.UI.Application.Navigation;`を追加します。通常のApplication／driverへNavigation依存を追加しません。
