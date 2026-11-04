# TSF COM declarations

`generate-tsf-interop.py TextStor.h msctf.h`をrepository内で実行します。Windows SDK header、または[microsoft/win32metadataのRecompiledIdlHeaders](https://github.com/microsoft/win32metadata/tree/main/generation/WinSDK/RecompiledIdlHeaders/um)を入力とします。ネットワーク取得をscript内で行いません。

IUnknownを除く継承methodをnative vtable順に展開します。HRESULTをpreserveし、opaque COM pointerをnint、既知の値型を固定幅型へ対応付けます。出力の順序をformatterで変更しないでください。ABI structはInterop内の手書き宣言をSDKと照合し、source-generated COM/AOT buildとmanaged fixtureで確認します。実際のTIPとの互換性は別途Windowsで検証します。
