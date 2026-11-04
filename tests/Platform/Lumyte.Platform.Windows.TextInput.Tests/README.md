# Managed TSF text-store fixture

```sh
dotnet test tests/Platform/Lumyte.Platform.Windows.TextInput.Tests -c Release
```

TSF/OS APIを呼ばず、ITextStoreACP実装のread/write lock、再入lockと非同期upgrade、rollback、readonly gate、UTF-16 buffer、QUERYONLY/NOQUERY、座標と古いlayout revisionを検証します。Linuxでも動かせるfixtureであり、Windows実機のTSF/TIP試験とは別です。

CsWin32のnative vtableからCCWを呼び、ITextStoreACP→managed実装→ITextStoreACPSinkのlock callbackの往復と、composition/edit sinkのIUnknown同一性も検証します。これは実TSFへの登録や日本語TIPの動作確認を代替しません。
