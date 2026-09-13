# Native mesh / amplification の実機試験

`DirectX12NativeMeshTests` は `Category=DirectX12Conformance`、`Requires=MeshShaders` として分離する。
Direct3D 12 の mesh 対応 device を必要とし、非対応機で成功扱いにはしない。`DirectX12NativeMeshFeaturesTests` は GPU 不要で、非対応 tier の nullable limits と対応 tier の写像を確認する。

shader はテスト内の HLSL から、既存の `DirectX12NativeComputeTests.Compile` と Microsoft.Direct3D.DXC の `dxc.exe` で生成する。
entry/profile は `meshMain/ms_6_6`、`amplificationMain/as_6_6`、`pixelMain/ps_6_6`、間接引数生成は `computeMain/cs_6_6` とする。製品 backend は compiler と shader reflection を必要とせず、raw DXIL のコピーを保持する。

root は既存の graphics root signature の `b0, space0` に直接送る256 byteである。depthはoffset0、descriptor indexは4、greenは16、blueは20、redは252。
直接MSでは red と green をMSが読み、blueをPSが読む。AS経由ではredをASが読んでpayloadに渡し、MSがそのpayloadと自身のrootのgreenを出力し、PSが自身のrootのblueを加える。
shader bytesとcolor target配列をpipeline作成後に書き換え、rootを記録後に書き換えても、元の入力で描画されることを読み戻して確認する。

直接・間接の各試験ではASなし／ありの両方で `(2, 3, 2)` groupsを起動し、最初のstageが各group IDをUAVに記録する。
間接試験ではcompute shaderが3個の`uint`だけをGPUへ書き、12 byteの範囲を`DispatchMeshIndirect`へ渡す。heap内のregion配置、descriptor range、実際の引数rangeにはそれぞれ非ゼロoffsetを使う。
描画結果と12個すべてのgroupの書込みを確認し、rootやdispatch数をCPUで補正しない。

ほかにline出力、pixel shaderなしのdepth描画、vertexとmeshの切替、depth/stencil PSO variantの再利用、使用時までのPSO生成延期、PSO生成失敗時のbatch全体の未実行、論理引数範囲と記録状態、破棄済みpipelineを確認する。
mesh/amplificationのUAV書込み後は公開stage指定から`VERTEX_SHADING`同期へ写像し、明示的なcopyとcompletion後に読み戻す。

標準のgrid上限は各軸65,535、積4,194,303、output vertices/primitivesは各256、payloadは16KiBとする。
積の値は現在の[DirectX-Headers](https://github.com/microsoft/DirectX-Headers/blob/main/include/directx/d3d12.idl)の`D3D12_MS_DISPATCH_MAX_THREAD_GROUPS_PER_GRID`に従う。
[拡大1D dispatch](https://microsoft.github.io/DirectX-Specs/d3d/D3D12IncreasedDispatchDimension.html)の条件付き上限は今回公開せず、shader・実際のcount・payloadの合法性はnative validationへ委ねる。

通常の実機試験はdebug layerを有効にしない。この試験の成功をdebug layerによる適合性検証の完了とは扱わない。
