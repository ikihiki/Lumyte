# Lumyte.Graphics.Validation

明示的に選ぶmanaged検証層です。ValidationDeviceはIGraphicDeviceを実装し、backendとqueueを借用して装飾します。Debug／ReleaseともrawはValidationEnabledがfalse、wrapperはtrueです。raw backendに検証切替APIや診断trackerを追加しません。

## 使用方法と所有権

    using Lumyte.Graphics.Abstractions;
    using Lumyte.Graphics.Validation;
    using Lumyte.Graphics.Vulkan;

    using var raw = VulkanDevice.Create();
    using var validated = new ValidationDevice(raw);
    IGraphicDevice device = validated;
    using IGraphicsBuffer<uint> upload = device.CreateBuffer(new BufferDesc<uint>
    {
        Count = 16,
        Usage = BufferUsage.CopySource,
        Memory = MemoryPreference.Upload,
    });

すべての子資源はdeviceから生成します。buffer、shader-data buffer、texture／view、sampler、argument table／GpuRef、shader／pipeline、command／render・compute encoder、queue／submission／semaphoreを同じdomainで包みます。desc内のwrapperもbackendの具象cast前にunwrapします。raw子資源の後付け採用、別domain、rawとの混在は拒否します。domain内のraw objectは一つのcanonical wrapperへ対応付け、GetElementも論理参照を返し、native adapterの具象型を漏らしません。

ValidationDevice.DisposeはbackendをDisposeしません。所有する子資源を先に解放し、次にwrapper、最後にrawを解放します。DeviceLostはsenderをwrapperとして転送し、Dispose時に購読解除します。内部の静的global dictionaryや解放済み資源の永久保持は設けません。同期は利用者の責務で、暗黙GPU待機、pin、lockを追加しません。raw参照やreflection経由の操作は診断を迂回します。security sandboxではありません。

## Surfaceとframe

IGraphicDeviceにはsurface factoryがありません。backend固有factoryで新規作成した同一backendのrawSurfaceを保持し、次のように借用wrapperを作ります。取得済みframeや過去のacquire状態は取り込めません。

    // rawSurfaceはbackend固有factoryで作成し、呼び出し側がusingで保持する。
    using var surface = validated.WrapSurface(rawSurface);

surface wrapperのDisposeはrawSurfaceをnative Disposeしません。swapchainとframeもwrapper経由で作ります。frame imageは借用で、呼び出し側が直接Disposeしてはいけません。作成したviewを解放し、明示的にframe.WaitForReleaseAsyncを待ち、frameをDisposeします。この待機はsubmission完了を意味しないため、GPU submissionも別途待機してください。

## 転送と依存metadata

宣言ReferenceCountの意味検査は検証層だけが担当します。ValidationArgumentsは元codecを呼ぶ前に`0 <= ReferenceCount <= TransferSize / 16`を確認し、不正値はArgumentOutOfRangeExceptionで拒否します。ValidationWriterは余分なWriteReferenceをnative writerへ渡す前にInvalidOperationExceptionを返し、不足は元codec終了後かつnative root発行前のCompleteでInvalidOperationExceptionとします。利用者producer／codecと各参照getterは一回だけ評価し、数値Captureを行いません。ReferenceCount getterが例外を投げる手書き型も検証層ではその例外が伝わりますが、raw Set／型付きCopyFromはgetter自体を読みません。rawでは宣言値が誤っていても実際のwire bytesが合法なら成功し得ます。

符号化する各参照の論理要素数Countは宣言ReferenceCountとは別です。checked uint変換、address／offset演算、実byte容量と完全サイズ・完了状態は全modeで必須です。rawに宣言参照数のfield・factory引数・比較を残さず、登録slot／range／access・table metadataは変更しません。CopyFromの失敗は部分byte更新を許しますが、検証層はbuffer全体の依存を無効化し、全体の再転送だけで回復します。

`ValidationArguments<T>`は静的な名前・サイズ・ReferenceCount・TransferFieldsを転送し、ValidationWriterがnative writerへstreaming転送します。利用者のcodecとgetterは一回だけ実行し、論理`IGpuRef<T>`を必要なnative element adapterへunwrapします。診断用adapter配列のコピーは許容しますが、数値Capture／Snapshotを中間入力にしません。Vulkan／D3D12のSet中の即時native定数記録は維持します。zero allocationや性能改善は主張しません。

Graph prepare callbackはadapter内でnative発行より前に実行し、失敗時は前回成功したroot選択を維持します。SetArgumentValidationの非null hookは検証層だけが扱い、rawでは未対応です。検証にはwrapped IGraphicDeviceを渡してください。

root参照、metadata tracker、buffer全体のqueryと非公開command overlayは検証層が所有します。raw TryGetDependenciesは常にfalse／nullです。wrapperの参照ゼロ型はtrue／既知空、参照付き未初期化・転送失敗後は未知でqueryを拒否し、全体の再転送でのみ回復します。部分更新は保守的union、全体更新は新revisionとし、記録済みidentity／source epochは不変です。GPU copyはnative記録成功後にoverlayへ反映し、Submit成功後だけ公開します。要素別追跡やqueue状態の複製でsource identityを作り替える処理は行いません。

## Queueとpresentationの失敗

記録、Submit、Acquire、Presentはnative前に事前照合し、成功後だけ診断状態をcommitします。キャンセルされた待機はnative使用完了ではありません。不透明なnative Submit例外ではwait／execute／signalの部分発行を否定できないため、元例外と候補operandを保持し、queueを未知状態として保守的に隔離します。健全queueの安全な回復APIはなく、明示的なbackend device-loss retirementだけが未知使用を退役させます。これはbackendの所有権取得やdevice自動Disposeではなく、新しい汎用通知APIも追加しません。

native Acquire成功が確定した後にframe wrapper構築が失敗した場合、native frameのDisposeが成功しても発行済みsemaphore signal状態は残ります。再利用前に明示的なwaitで消費してください。rollback cleanupにも失敗した場合はraw leaseを保持・隔離します。不透明なacquire例外はswapchainを隔離し得ますが、実行した根拠のないsemaphore消費を作りません。

## 境界と移行

ValidationプロジェクトはAbstractionsとSharedのhelperだけを参照し、四つのnative backendはValidationを参照しません。IValidatedShaderReferenceはこのnamespaceへ移動します。rawに残るhost span bounds、overflow、mappingのmemory safety、ABI offset／符号化する論理要素数Count、実際のwire容量・完了状態、root／program状態、driver APIとartifact容量の検査は任意診断ではありません。Windows D3D12のnative失敗時のfence owner保持も必須の正しさとして残します。

GraphはwrapperのValidationEnabledを使い、従来のallocation／元image identityとread／write権限の粗い宣言照合だけを追加します。rawではpass宣言だけからbarrier・順序・cullingを決めます。全backendのrootなし全table binding、有限descriptor／registration／remap上限は変更せず、boundはusedを意味しません。旧固定checkpointの測定はこの層の性能結果ではありません。
