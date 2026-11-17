# メニューと受動ツールチップ

公開Headless UiApplication、明示fontと借用Parley／text backendを使う実行例です。
Menu・MenuItemのcommand、boolチェック、submenu factory、Escape・外側終了、focus復帰を通常の排他的popup入力leaseで確認します。
Tooltipは明示的なIsOpenの変化で開閉し、実際のTextBox接続のfocusとpreeditを維持します。通常のpointer入力も背面のanchorへ届きます。

```sh
dotnet run --project samples/Lumyte.Samples -c Release -- --sample ui-menus --backend headless --configuration Release -- tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf
dotnet run --project build -c Release -- --target TestUiMenus
dotnet run --project build -c Release -- --target TestUiMenusAot
```

Menuは既存Popupの通常内容であり、表示のisOpenを変える責務はアプリにあります。
commandとsubmenuの同時指定、チェックとsubmenuの同時指定、MenuItem以外の直属項目は拒否します。Separatorは既存の型を使います。
Tooltipのhover／focus triggerや遅延timerはこのAPIに含めず、明示的な表示モデルへ分離します。
配置は従来の名前付き内容とwith引数で指定し、追加のGridCellやExpanded wrapperを設けません。

診断の実runtimeはschema 1〜9の閉じたsnapshot形状と、新roleによる通常Check入力を生成metadataで確認します。
借用backendは決定的な検証用で、OS IMEではありません。Windows、Browser、物理表示とGPU提示は未検証です。
