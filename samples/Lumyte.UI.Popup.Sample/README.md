# Hosted Popupの操作・合成描画サンプル

```sh
dotnet run --project samples/Lumyte.UI.Popup.Sample -c Release -- none
dotnet run --project samples/Lumyte.UI.Popup.Sample -c Release -- vulkan popup-vulkan.png
dotnet run --project samples/Lumyte.UI.Popup.Sample -c Release -- wgpu popup-wgpu.png
```

一つの提示rootへ通常画面・Popup・submenuを合成します。UiPopupAutomationSessionでClick／Enter／submenuを操作し、保存回数3件と終了処理を確認します。内容はusing static ComposeによるComposition、入力は同じUiPopupManagerを通します。専用検証モードのため物理入力を転送しません。

GPUモードは合成したroot targetを実際に描画・readbackし、Popup矩形を切り出したPNGを保存します。PNGは180×100、最初の保存項目は160×32の緑です。GPU処理中の資源を先に解放せず、完了後に提示revisionを通知します。利用中のGPU backendとSlangの実行環境を用意してください。

Browserは既存[Browserサンプル](../Lumyte.UI.Browser.Sample/README.md)の?popupsで同じ内容を描画します。追加canvas/tabは作りません。

これは専用サンプルです。一般アプリへ組み込む場合は、IUiWindowSourceの合成登録・GPU退役、IUiAutomationHostの物理入力domainの排他、中立待ち・保留・取消清掃をアプリのloopに接続してください。

2026-10-29にnone、Vulkan、wgpuで保存回数3件と終了を確認しました。GPUはMesa lavapipeで実行し、両PNGの180×100寸法、CRC、緑5,120画素が一致しました。実機GPU／Nativeの別Window操作の検証とは区別します。
