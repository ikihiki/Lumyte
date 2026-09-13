# Native timeline と非同期 copy の実機試験

`DirectX12NativeTimelineTests` は `Category=DirectX12Conformance` の実 GPU 試験として分離する。
DirectX 12 の `DIRECT` と `COPY` command queueを使い、CPUによるwaitやsignalはdevice所属のsemaphoreで行う。

CPU先行提出の試験は、CPUがまだsignalしていないgateでcopy queueを止め、3 frameのcopyとcomputeを両queueへ提出する。
各frameは同じGPU入力rangeを使うため、copyは前frameの描画完了、computeは対応frameのcopy完了をGPUで待つ。
別のCPU threadは最初のframeを`WaitCpu`する。全frameを提出した時点ではgateが閉じているので完了していないことを確認し、最後に`SignalCpu`でgateを開いて3個の計算結果を読み戻す。
時間計測、sleep、GPU速度による順序の推測は使用しない。

texture uploadはMainの`Undefined → Common`初期化、Copyによるupload、Mainの`Common → ShaderRead`遷移とcompute参照を、それぞれtimeline pointで接続する。
texture readbackはMainのrender passでclearし、`ColorAttachment → Common`に遷移してからCopyが読み戻す。
copy queue内ではtexture layout transitionを発行しない。`Common`はnative `D3D12_BARRIER_LAYOUT_COMMON`であり、既存の`General`は`DIRECT_QUEUE_COMMON`のまま保持する。
参考: [Enhanced Barriers: Copy Queues](https://microsoft.github.io/DirectX-Specs/d3d/D3D12EnhancedBarriers.html#copy-queues)。

empty submissionによる将来のproducer待機、複数wait pointとcaller配列の変更、CPU signal、破棄済み・別deviceのtimeline、別queueのrecording、PSO生成失敗前のGPU wait未挿入も確認する。
CPU waitはqueueのcommand memoryを回収しないため、完了したcaller semaphoreを破棄した後の次のSubmitが内部completionのみで回収できることを試験する。

compute shaderは既存のDXC helperで`cs_6_6`をコンパイルする。backendにshader compilerやreflectionは追加しない。
rootのCPU byte spanは各workの記録呼出し中にコピーされ、呼出しが戻れば変更・再利用できる。
upload staging、GPU region、texture、descriptorとtimelineはcallerが対応するCPU／GPUの使用完了まで保持し、backendや関連objectを先に破棄しない。

CPU timeline操作のnative結果を受ける小さな入口には、device removed/resetと通常のout-of-memoryを注入し、前者だけが別queueにもdevice lossとして伝わることを確認する。
native `Wait`／`Signal` の失敗とdevice lossを実driverへ強制する試験は含めない。通常実機試験はdebug layerを有効にせず、その検証完了を意味しない。
