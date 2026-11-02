# Browser DI pages sample

`dotnet publish samples/Lumyte.UI.Pages.Browser.Sample -c Release -o artifacts/browser/ui-pages`で出力し、`wwwroot`をHTTP配信します。Headless sampleと同じ`MapPage`／`MapGroup`のclass・関数ページを使い、Scopeの生成・破棄回数も検査します。

Chromiumを用意し、`node samples/Lumyte.UI.Pages.Browser.Sample/verify-browser.mjs artifacts/browser/ui-pages/wwwroot`で通常クリック、GPU PNGの画素、Window IDの維持、Surface IDの交換、URL/historyの維持、終了を検証します。Browserのprobeと検査scriptはNavigation sampleと共有し、検証用のJS bridgeをアプリpackageへ公開しません。
