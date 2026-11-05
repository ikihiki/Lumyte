# Lumyte.UI.Media

任意の表示専用部品です。UI coreからMedia、Resources、Platform、DI、テーマへの依存はありません。
一般文字は編集文書なしのITextLayoutProviderが不変TextLayout2Dを供給します。
実フォントを使用するadapterはLumyte.UI.Media.Parleyです。

```csharp
using Lumyte.UI;
using Lumyte.UI.Media;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

// engineは呼出し元が所有し、先にAddFontでフォントを明示登録する。
var provider = new ParleyTextLayoutProvider(engine);
var content = Stack(spacing: 4)[
    Text(value: "A paragraph", layoutProvider: provider, width: 160,
        with: [Padding.All(8)]),
    Image(source: preparedImage, width: 80, height: 40,
        stretch: ImageStretch.Uniform, clipToBounds: true),
    Icon(source: iconSource, size: 20, automationName: "保存"),
    ProgressBar(value: 0.5, automationName: "完了率"),
    BusyIndicator(automationName: "読み込み中")
];
```

## 文字

Textは最終本体幅で同期のCreateLayoutを一度呼び、advance寸法を親制約へclampします。
RecordとCurrentLayout getterはproviderを再実行しません。
値/provider/幅が変われば再文字組みし、snapshotはfont/engineの解放後も独立して使えます。
providerは必須、文字列nullと不正幅は拒否します。0幅は描画なしのsnapshotです。
Paragraph optionsの変更には新しいproviderを明示します。mutableなproviderの暗黙更新監視はありません。

## 画像とGPU寿命

```csharp
// sampled view、samplerはアプリが準備済み。texel sourceとlogical sizeは別に指定する。
var preparedImage = new PreparedImage(
    new Lumyte.Graphics.Drawing2D.Image2D(view, sampler),
    new System.Numerics.Vector2(100, 50),
    new Lumyte.Graphics.Drawing2D.Rect2D(0, 0, 200, 100));
var iconSource = IconSource.Image(preparedImage);
```

ImageはSource=nullなら描画せずPlaceholderSize（既定0）を占有します。intrinsic logical寸法は正のfinite、
source矩形はviewのmip内の正のfinite texel領域です。
片軸tightでは他軸の希望寸法を比率から計算し、親制約を優先します。
Noneは原寸、Fillは非等方、Uniformは内接、UniformToFillは外接です。配置は中央、既定clipはtrueです。

**資源はすべて借用**です。Image/Iconの差替え、unmount、UiSurface.Dispose、DrawingSnapshot2D.Disposeは
texture/view/sampler/resource leaseのGPU完了を意味しません。
アプリは直接DrawList、retained scene、captureとそのprepared frameの最後のGPU完了まで元ownerを保持します。
正常終了順はGPU完了→command→prepared/scene/capture→view/sampler/allocation/leaseです。
Prepare失敗では未送信commandを破棄し、Wait取消ではownerを退役待ちへ残します。UIはWaitもDisposeも代行しません。
既存のUI Sharedのframe coordinatorとResources.Graphics.Drawing2Dの世代leaseをそのまま使用できます。

## Icon、進捗、時間

IconSource.Vectorは不変Path2Dとpaint、Glyphは実際に文字組み済みTextLayout2D、ImageはPreparedImageです。
vector/glyphのviewBoxは明示の正寸法、Icon.Sizeは正のfinite（既定16）です。親tight制約が優先し、
本体内へUniform配置します。名前はAutomationNameへ指定します。Button動作はなく、装飾用の空名も許可します。

ProgressBar.Valueはfiniteの0〜1だけを受け、範囲外をArgumentOutOfRangeExceptionで拒否します。
既定要求寸法は120×4、色はForeground/Backgroundから明示でき、callbackや値編集はありません。
通常のUiArgumentにより`Signal<double>`も同じvalue引数へ渡せます。

BusyIndicatorは既定要求寸法24×24、Colorを明示できます。標準hostの借用AnimationClockとowner loopだけで動きます。
埋込みhostではlayout前にUiSurface.AdvanceAnimationsを一回呼びます。独立timer/threadは作りません。
visible/mountedの期間だけ進み、hidden/unmounted/surface休止ではframe要求を止め、再開時catch-upしません。
Phase、Record、layout、HasRunningAnimations等のgetterは時間を進めません。

5部品すべてAutomationActions.Noneであり、roleやFocusableの指定でactivationを獲得しません。
ProgressBarはValue、BusyIndicatorはIsIndeterminateをimmutable snapshotとwire clientへ公開します。

## 実行と制限

[sample](../../../samples/Lumyte.UI.Media.Sample/README.md)は実フォントを渡して標準Headless hostで動く公開APIのscenarioです。
共通runnerのTestUiMediaと各test projectで検証します。
Textは選択/編集なし、Imageはload/decodeなし、IconはButtonなしです。
Windows、Browser、NativeAOTとGPUの実行可否は各実行記録で区別します。
