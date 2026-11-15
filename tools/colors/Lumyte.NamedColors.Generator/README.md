# 名前付き色の保守tool

`CanonicalColors.json`は既存の149件のCSS名・field名・RGBA byteコードを抽出した唯一の保守元です。
`ReceiverMatrix.csv`と`src/Core/Lumyte.Mathematics/Colors/NamedColors/*.g.cs`は生成物です。
外部palette packageを増やさず、既存Math kernelでoffline計算します。

## 実行

リポジトリルートで、通常のMath build後に実行します。共通runnerの登録は`build/README.md`を参照してください。

```sh
dotnet build tools/colors/Lumyte.NamedColors.Generator -c Release -m:1
dotnet tools/colors/Lumyte.NamedColors.Generator/bin/Release/net10.0/Lumyte.NamedColors.Generator.dll
dotnet tools/colors/Lumyte.NamedColors.Generator/bin/Release/net10.0/Lumyte.NamedColors.Generator.dll --verify
dotnet tools/colors/Lumyte.NamedColors.Generator/bin/Release/net10.0/Lumyte.NamedColors.Generator.dll --verify --verify-native-linear
```

通常のgenerate/verifyはnative Powを呼びません。`--verify-native-linear`だけが現在hostの512個のfloat/double bitsを比較し、不一致を失敗として報告します。
既存kernelのoracleや許容値は変更しません。基準更新は別操作として明示的に実行し、derived dataと生成物のdiffをレビューします。

```sh
dotnet tools/colors/Lumyte.NamedColors.Generator/bin/Release/net10.0/Lumyte.NamedColors.Generator.dll --refresh-linear-reference
```

## matrixと数値規則

- S/L各8配置×byte・ushort・Half・float・double・decimal。RGBAのみsbyte・shortも提供します。
- byte/ushortはUNORM255/65535。sbyte/shortは正のSNORM127/32767。正規化・最近接偶数量子化は既存kernelを利用します。
- alphaなしは148色、alphaありはtransparent blackを含む149色。Gray/GrayAlphaはR=G=Bの名前だけです。
- SHsla/SHsvaはHalf・float・double・decimalのみ。canonical byteをdecimalへ正規化してから既存の厳密model変換をoffline利用し、最後にreceiver精度へ丸めます。
- Raw packed 5形式はS/L prefixで色空間を明示し、RGB565だけTransparentを持ちません。SRgba8は149色です。
- `LinearReference.json`は256コードのbinary32/64 bitsとprovenanceを固定します。floatはbinary32、その他のlinear正規化表現はbinary64から既存checked数値変換で導出します。decimalもこのbinary64からのchecked decimal変換です。
- encoded正規化は既存の厳密UNORM比変換、linear integerとpackedは固定binary64 referenceから既存量子化を使います。

## 実行時と公開API

各閉receiverにつき非generic public static extension containerを一つ生成します。同名のget_Redが同一outer containerに衝突するため、単一containerに集約しません。
各getterはprivate nested cacheのreadonly structをcopyするだけです。除算・transfer・量子化・constructorをlookup時に実行しません。
ただしCLRの初回cache初期化ではbaked literalからconstructorを一度呼び、Halfのbitsも一度復元します。literal constとは主張しません。
C#14 compilerが生成するextension宣言・marker metadataはcompilerの役割であり、任意のpublic helperを追加する仕組みではありません。

基準はSDK 10.0.403/.NET 10.0.14/Linux x64です。ARMその他のnative transfer一致は未検証で、固定tableを変更する理由にしません。

生成規模は114閉receiver・14,453 property・約3.8 MiBのC#です。通常SDKで分割sourceをcompileし、analyzer・warning規則を緩和しません。
ビルドのwall time/RSS計測はcompiler server再利用の影響を含むため、cold compiler peakの保証ではありません。
