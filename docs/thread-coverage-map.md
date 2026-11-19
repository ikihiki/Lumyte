# Thread-dependent test coverage map

Baseline: fa7e3aa8. Remaining Cpu methods still use the method-parallel runner.

Integration means actual worker execution, not async, a task-shaped return, a semaphore, or a fake/cooperatively pumped scheduler alone. Native/Gpu/default classifications retain precedence. All changes use method traits; no project default is broadened.

## Callgraph findings

- Core and graphics ResourceExecutionHost configure ThreadPool workers (2 and 1 respectively). Fixture/StreamHarness consumers are Integration even without explicit Task.Run in their body.
- ExecutionFixture and WorkflowFixture default to Host + Cooperative: these stay Cpu. Dedicated placement, ThreadPool, explicit producers and cross-thread notifications move. Two mixed theories split into cooperative Cpu and ThreadPool Integration facts sharing unchanged implementations.
- UiApplication.RunAsync creates a dedicated UI thread for headless/non-current-thread drivers. PageFixture, NavigationHarness and actual application benchmark lifecycle callers move; passive Build, fake frame gates and inline runtime tests stay Cpu.
- LoopbackHandler owns a thread. Its consumers move; manually pumped diagnostics harness tests remain Cpu.
- ThreadChunk cases using RunOnThreadAsync/StartThread move; oversized allocation without a worker stays Cpu. Parallel, Task.Run and thread-affinity/race cases move even if operations are individually sequential.
- Benchmark aggregators are followed through memory contention, signal ParallelWave, resource worker runtimes and dedicated execution producers.

## Counts

Source-enumerated facts/InlineData rows, not expanded MemberData discovery counts. MemberData theories count as one declaration. Runtime discovery is authoritative for expanded totals.

Non-Tools catalog totals: Cpu 4094 → 3676; Integration 314 → 732; Gpu 158 → 158.
Projects advertising Integration: 13 → 31 for this non-Tools change; parent runner integration brings the final catalog to 32. After runner-proof consolidation CPU has 49 projects, GPU 10 (host selectors exclude unsupported OS projects).

| Project | Cpu before → after | Integration before → after |
| --- | ---: | ---: |
| tests/Animation/Lumyte.Animation.Tests/Lumyte.Animation.Tests.csproj | 147 → 146 | 0 → 1 |
| tests/Audio/Lumyte.Audio.Benchmark.Tests/Lumyte.Audio.Benchmark.Tests.csproj | 9 → 8 | 3 → 4 |
| tests/Core/Lumyte.Core.Benchmark.Tests/Lumyte.Core.Benchmark.Tests.csproj | 110 → 103 | 0 → 7 |
| tests/Core/Lumyte.Execution.Tests/Lumyte.Execution.Tests.csproj | 99 → 72 | 0 → 27 |
| tests/Core/Lumyte.Mathematics.Tests/Lumyte.Mathematics.Tests.csproj | 277 → 276 | 0 → 1 |
| tests/Core/Lumyte.Memory.Tests/Lumyte.Memory.Tests.csproj | 27 → 15 | 0 → 12 |
| tests/Core/Lumyte.Resources.Tests/Lumyte.Resources.Tests.csproj | 225 → 89 | 0 → 136 |
| tests/Core/Lumyte.Signals.Tests/Lumyte.Signals.Tests.csproj | 52 → 47 | 0 → 5 |
| tests/Diagnostics/Lumyte.Diagnostics.Tests/Lumyte.Diagnostics.Tests.csproj | 35 → 34 | 0 → 1 |
| tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/Lumyte.Diagnostics.UI.Automation.Tests.csproj | 149 → 62 | 0 → 87 |
| tests/Graphics/Lumyte.Graphics.Benchmark.Tests/Lumyte.Graphics.Benchmark.Tests.csproj | 78 → 77 | 4 → 5 |
| tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/Lumyte.Graphics.Drawing2D.Tests.csproj | 443 → 441 | 19 → 21 |
| tests/Graphics/Lumyte.Graphics.Hosting.Tests/Lumyte.Graphics.Hosting.Tests.csproj | 12 → 11 | 0 → 1 |
| tests/Graphics/Lumyte.Graphics.RenderGraph.Tests/Lumyte.Graphics.RenderGraph.Tests.csproj | 141 → 140 | 0 → 1 |
| tests/Graphics/Lumyte.Graphics.Tests/Lumyte.Graphics.Tests.csproj | 309 → 304 | 87 → 92 |
| tests/Graphics/Lumyte.Resources.Graphics.Tests/Lumyte.Resources.Graphics.Tests.csproj | 48 → 4 | 0 → 44 |
| tests/Input/Lumyte.Input.Advanced.Tests/Lumyte.Input.Advanced.Tests.csproj | 112 → 108 | 0 → 4 |
| tests/Maps/Lumyte.Maps.UI.Tests/Lumyte.Maps.UI.Tests.csproj | 13 → 10 | 0 → 3 |
| tests/UI/Lumyte.UI.Application.Navigation.Tests/Lumyte.UI.Application.Navigation.Tests.csproj | 27 → 10 | 0 → 17 |
| tests/UI/Lumyte.UI.Application.Tests/Lumyte.UI.Application.Tests.csproj | 134 → 102 | 0 → 32 |
| tests/UI/Lumyte.UI.Benchmark.Tests/Lumyte.UI.Benchmark.Tests.csproj | 50 → 47 | 4 → 7 |
| tests/UI/Lumyte.UI.Navigation.Tests/Lumyte.UI.Navigation.Tests.csproj | 45 → 26 | 0 → 19 |
| tests/UI/Lumyte.UI.Tests/Lumyte.UI.Tests.csproj | 489 → 481 | 0 → 8 |

## Moved method inventory

303 existing named methods change Cpu → Integration; additionally the two split theories move only their ThreadPool cases.

### tests/Animation/Lumyte.Animation.Tests/AnimationSourceEvaluationTests.cs

- ParallelSamplesUseIndependentContexts

### tests/Audio/Lumyte.Audio.Benchmark.Tests/DecodeAndResourceTests.cs

- CpuStorageControlEnforcesContract

### tests/Core/Lumyte.Core.Benchmark.Tests/ExecutionCompositionTests.cs

- VerifyConsumers
- DedicatedProducersDrainAllCommandsOnSerialSchedulerAsync

### tests/Core/Lumyte.Core.Benchmark.Tests/NumericMemoryTextTests.cs

- MemoryContracts

### tests/Core/Lumyte.Core.Benchmark.Tests/RuntimeWorkloadTests.cs

- IndependentStreamsRestartAndCompleteAsync

### tests/Core/Lumyte.Core.Benchmark.Tests/SignalsResourcesSettingsTests.cs

- TargetOracles

### tests/Core/Lumyte.Execution.Tests/DedicatedLoopTests.cs

- PendingControlsContinueBeforeTheNextPeriodicUpdate
- DueUpdatesRunWhileControlsRemainQueued
- OpeningAGateWakesAnIdleOwner
- CancellationWakesAnOwnerWhileItsGateRemainsClosed

### tests/Core/Lumyte.Execution.Tests/DemoIntegrationTests.cs

- ParameterlessDiRegistrationDrivesGeneratedVoiceOnDedicatedOwner

### tests/Core/Lumyte.Execution.Tests/GraphLifetimeTests.cs

- ShutdownWaitsForRunningRangeAfterCancellationAsync

### tests/Core/Lumyte.Execution.Tests/GraphTests.cs

- ThreadPoolRangesRunInParallelWithinLimit
- OwnerGateWaitDoesNotBlockIndependentCpuCommand
- CancellationWakesGatedThreadPoolCommand

### tests/Core/Lumyte.Execution.Tests/ModuleCollectionTests.cs

- QuiescingStartupContextCanFinishCleanupAsync

### tests/Core/Lumyte.Execution.Tests/RuntimeTests.cs

- PreparationAndFixedHostAreRequired
- FailedCleanupKeepsOwnerForRetry
- CancellationAfterStartRetainsActualResult
- MultipleProducersDoNotLoseCommands
- DedicatedOwnerDrivesUpdatesIndependently

### tests/Core/Lumyte.Execution.Tests/SchedulingTests.cs

- NewCpuCommandUsesAvailableConcurrencyAsync
- RequeuedRangesCompleteBeforeShutdownAsync

### tests/Core/Lumyte.Execution.Tests/ShutdownTests.cs

- GateNotificationsCanRaceWithShutdownAsync

### tests/Core/Lumyte.Execution.Tests/WorkflowTests.cs

- WaitingWorkflowDoesNotHoldCpuSlot
- ForeignResultReturnsToWorkflowScheduler

### tests/Core/Lumyte.Mathematics.Tests/CssColorsTests.cs

- ConcurrentLookupMatchesIndependentFixture

### tests/Core/Lumyte.Memory.Tests/MemoryManagerTests.cs

- ConcurrentReservationsShareOneBudgetAsync

### tests/Core/Lumyte.Memory.Tests/ThreadChunkTests.cs

- SameThreadRentsShareChunkAsync
- DifferentThreadsUseDifferentChunksAsync
- ManagerPoolAndElementTypeHaveSeparateChunksAsync
- SharedChunkLivesUntilLastScopeReturnsAsync
- RemoteReturnDoesNotReusePartiallyReleasedChunkAsync
- RolloverCannotExceedHardBudgetAsync
- EmptyChunkIsReleasedBeforeNextRentAsync
- ConcurrentRemoteReturnsReleaseChunkOnceAsync
- LastRemoteReturnCanRaceOwnerRentAsync
- ConcurrentThreadChunksRespectHardBudgetAsync
- ConcurrentScopeCloseAndRentLeavesNoChargesAsync

### tests/Core/Lumyte.Resources.Tests/EmbeddedResourceSourceTests.cs

- PipelineUsesEmbeddedSourceThroughDependencyInjectionAsync

### tests/Core/Lumyte.Resources.Tests/FileResourceChangeSourceTests.cs

- FileSourceAutomaticallyReloadsItsOwnStreamsAsync

### tests/Core/Lumyte.Resources.Tests/ResourceAcquisitionLifetimeTests.cs

- PendingAcquisitionRetainsReplacementAcrossReloadAndTrimAsync
- CanceledWaiterDoesNotLeakOwnershipOrCancelOtherWaitersAsync
- FailedBuildReturnsAllPendingOwnershipBeforeRetryAsync

### tests/Core/Lumyte.Resources.Tests/ResourceConcurrencyTests.cs

- AcquireAfterTrimDoesNotJoinPublishedLoadWithPendingCleanupAsync
- CachedMetadataDetectsCycleThroughTrimmedBuildDependencyAsync
- ReloadAllowsAcyclicDependencyDirectionToReverseAsync
- RuntimeAdvanceWithoutInvalidationPreservesOlderLeasesAsync
- ReplacingMetadataEdgePreservesOldLeaseAndPrunesRetiredInputAsync
- FailedLoadRemovalRetriesMetadataPruningAfterConcurrentTrimAsync
- LeaseReadsPreservePublishedValueAsync
- ResidentPathsPreserveDistinctVersionsAndBuildBoundariesAsync
- RuntimeInvalidationIsObservedBeforeManagerChangedCallbackAsync
- RuntimeContractViolationPoisonsOperationsWithoutThrowingFromEventAsync
- AdditionalOwnersDelayReleaseUntilDisposedAsync
- UnavailableLeaseAndViewChecksRejectInvalidatedGenerationAsync

### tests/Core/Lumyte.Resources.Tests/ResourceEnvironmentTests.cs

- CompositeEnvironmentTracksEveryDeclaredProviderAsync
- ExecutorSelectionIsFrozenThroughReleaseAsync

### tests/Core/Lumyte.Resources.Tests/ResourceHotReloadTests.cs

- DuplicateHintsAreDebouncedAsync
- ContinuousHintsReachMaximumBatchDelayAsync
- SynchronousContinuousSourceDoesNotStarveReloadAsync
- InitialPublicationAndSourceHintsShareOneNotificationSignalAsync
- NotificationDuringReloadIsRetainedAsync
- FailedReloadRetriesAndKeepsPreviousGraphAsync
- ExhaustedRetryAllowsFutureNotificationAsync
- ConflictRetriesLatestRevisionAsync
- RescanRevalidatesTrackedSourcesAsync
- WatcherFailureFaultsCompletionAsync
- ObserverFailureStopsWatcherAsync
- CancellationCallbackFailureStillJoinsWorkersAsync
- InitialPublicationChecksOnlyNewInputsAsync
- InitialPublicationRechecksAnAlreadyConsumedChangeAsync
- InitialPublicationsPreservePendingBatchDeadlineAsync

### tests/Core/Lumyte.Resources.Tests/ResourceInvalidationLifetimeTests.cs

- UnownedInvalidationIsForgottenAfterTrimAsync
- LiveReferencePreservesRecoveryAfterContentTrimAsync
- ParentDependencyPreservesRecoveryMetadataAsync
- FailedCleanupRetainsWatchUntilRetryCompletesAsync

### tests/Core/Lumyte.Resources.Tests/ResourceManagerTests.cs

- MultiStagePipelineReleasesBuildInputsAsync
- ManualConstructionAcceptsTypedStagesDirectlyAsync
- ResidentDiamondSharesLeafAndTrimsParentsFirstAsync
- BorrowedDependencyCannotBecomeAnotherOwnedOutputAsync
- DocumentProjectionsNormalizeInputAddressAsync
- ConcurrentAcquireSharesBuildAndIsolatesCallerCancellationAsync
- IndependentBuildTasksDetectDynamicCycleAsync
- ReleaseFailureRetainsMemoryAndDependenciesForRetryAsync
- RequiringBuildThenResidentPromotesOneSharedDependencyAsync
- FailedBuildCleanupRetainsChargeAndRetriesOnlyUnfinishedWorkAsync
- ReferenceAcquiresStableFrameLeasesAcrossReloadAsync
- RetainAndPinPreserveReferenceAndLeaseOwnershipAsync
- ManagerResolvesHandlesToOwningReferencesAsync
- ReferenceRejectsValuesMissingFromCapturedFrameAsync
- HandlesAreUnmanagedAndRejectExpiredOrForeignOwnersAsync
- ReloadPublishesAtomicallyAndKeepsOldLeasesAndSnapshotsAsync
- ReloadRebuildsOnlyChangedInputAndItsDependentsAsync
- FailedReloadPreservesPublishedGraphAsync
- ReloadRejectsSourceRevisionChangedDuringConstructionAsync
- ReloadTracksTrimmedBuildDependencyAsync
- RuntimeLossInvalidatesResidentsAndRebuildsExistingHandleAsync
- ShutdownRejectsReferencesLeasesAndSnapshotsAsync
- DependencyInjectionSharesManagerAcrossScopesAndOwnsServicesOnceAsync
- DependencyInjectionRetainsServicesUntilCleanupRetryCompletesAsync

### tests/Core/Lumyte.Resources.Tests/ResourcePipelineJobTests.cs

- OffWorkflowRequestDoesNotInlineDependency
- IgnoredCanceledDependencyCompletesBeforeItsWorkflowCloses

### tests/Core/Lumyte.Resources.Tests/ResourceRuntimeTests.cs

- DependencyInjectionSharesPerProviderAndIsolatesIndependentHostsAsync

### tests/Core/Lumyte.Resources.Tests/ResourceSourceStreamingTests.cs

- SourcesSupportStreamingAndResidentLifetimeAsync

### tests/Core/Lumyte.Resources.Tests/ResourceStageRuntimeTests.cs

- ParentAndChildUseTheirOwnSelectedRuntimeAsync
- FrozenSelectionTracksDeviceChangesAndRetainsReleaseOwnershipAsync
- ExplicitAndSelectedSameProviderAreRegisteredOnceAsync
- DifferentProvidersWithTheSameIdAreRejectedAsync
- InvalidStageBindingsAreRejectedAsync
- DependencyInjectionSelectsPerStageWithoutReplacingTheDefaultAsync

### tests/Core/Lumyte.Resources.Tests/ResourceStreamJobTests.cs

- ThreadPoolDecodersShareCpuConcurrencyLimit

### tests/Core/Lumyte.Resources.Tests/ResourceStreamTests.cs

- IndependentSessionsHaveBoundedPrefetchAsync
- ReloadPreservesExistingSessionAndUpdatesNewSessionsAsync
- ShortReadsAndEndOfStreamPreserveSequenceAsync
- LongStreamReusesFixedBufferBudgetAsync
- ReadCancellationOnlyCancelsItsWaitAsync
- CanceledReadNotificationsCanBeReusedAsync
- ReadAvailableConsumesBufferedSequenceWithoutReadingInputAsync
- ReleasingBlockWakesProducerWithoutInlineReaderAsync
- SingleBlockWakeupsPreserveSequenceAsync
- FullBufferDisposalStopsWithoutConsumptionAsync
- SeekReplacesPrefetchedDataAsync
- SeekWaitsForBlockedProducerToStopAsync
- FailedSeekFaultsSessionAsync
- ProducerFailureIsObservableAsync
- FailedDisposalRetainsBuffersUntilRetryAsync
- ConcurrentDisposalSharesCompletionAsync
- CreationCancellationDoesNotBecomeSessionCancellationAsync
- CancelledCreationReleasesSessionOwnershipAsync
- FailedReaderCreationReleasesAllocatedBuffersAsync
- EmptyStreamIsAtEndImmediatelyAsync
- ManagerRetainsFailedInitializationCleanupAsync
- UnsupportedSeekPreservesSessionAsync
- InvalidOptionsAreRejectedAsync

### tests/Core/Lumyte.Resources.Tests/ResourceTypeIdentityTests.cs

- ClosedGenericTypesAtTheSameAddressRemainDistinctAsync
- IdenticalTypeNamesInDifferentLoadContextsRemainDistinctAsync
- DuplicateOutputTypeRegistrationsAreRejectedAsync
- StaticInputTypeCycleIsRejectedAsync

### tests/Core/Lumyte.Resources.Tests/ResourceValueTypeTests.cs

- StructOutputsRetainTheirInputAcrossReferencesAndSnapshotsAsync
- StructReleaseFailureRetainsOwnerAndInputUntilRetryAsync

### tests/Core/Lumyte.Resources.Tests/ResourceWatchRegistrationTests.cs

- DirectManagerOwnsWatchingAndUpdatesLoadedTargetsAsync
- TrimmedBuildInputsRemainWatchedUntilTheirParentIsReleasedAsync
- SourcesWithoutNotificationsDoNotStartMonitoringAsync
- DisabledReloadDoesNotSubscribeToTheSourceAsync
- ScopesShareOneManagerAndSourceSubscriptionAsync
- ManagerDisposalStopsReloadBeforeReleasingOutputsAsync
- WatchFailureDoesNotPreventManagerCleanupAsync
- ReloadIgnoresUntrackedSourceHintsBeforeReadingRevisionsAsync

### tests/Core/Lumyte.Resources.Tests/ResourceWatchSharingTests.cs

- SharedSourceRemainsWatchedUntilLastPublicationIsTrimmedAsync
- FailedBuildPreservesPendingBuildSourceAsync
- ReloadReplacesWatchWithoutMutatingCapturedTargetsAsync

### tests/Core/Lumyte.Resources.Tests/TypedStageRuntimeTests.cs

- SourceAndTransformReceiveTheirDeclaredRuntimeAsync
- IncompatibleEnvironmentIsRejectedBeforeBuildAsync
- TypedStageCanUseTheDefaultEnvironmentAsync

### tests/Core/Lumyte.Signals.Tests/AsyncTests.cs

- PublicationReleasesTheGraphBeforeSchedulingAsync

### tests/Core/Lumyte.Signals.Tests/GraphTests.cs

- ValuesCanMoveBetweenThreadsWhenOperationsAreSequentialAsync

### tests/Core/Lumyte.Signals.Tests/InvalidationTests.cs

- DifferentThreadsKeepDistinctStampsAsync

### tests/Diagnostics/Lumyte.Diagnostics.Tests/OperationTests.cs

- OwnershipAndLifetimeAreExplicit

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/ClientTests.cs

- TypedClientCompletesGameSideOperationsAsync
- PopupPageSharesRootControlSequenceAsync
- LostAdmissionRetriesTheSameLogicalActionAsync
- LostAllAdmissionRepliesRemainOutcomeUnknownAsync
- ExpiredAdmissionPreservesUnknownOutcomeAndBlocksFurtherInputAsync
- BadHashAndObservationInputAreRejectedAsync
- MismatchedCatalogIsNotAcceptedAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/CollectionWireTests.cs

- RemoteListSelectionUsesNormalInputAndLatestCallbackAsync
- CollectionsDoNotChangeOldSnapshotShapesAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/LocatorVersionTests.cs

- TypedOperationsHonorAncestorSchemaAsync
- OlderCatalogRejectsNewRoleBeforeInputAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/MenuWireTests.cs

- RemoteMenuCheckingIsLeasedAndIdempotentAsync
- MenuRolesKeepEveryOldShapeAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/NumericTests.cs

- StrictOldReadersKeepTheirOriginalShapeAsync
- RemoteNumericCommandsUseRealControlsAndExactMetadataAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/SemanticValueTests.cs

- RemoteScrollRejectsOccludingSiblingAsync
- ClientScrollUsesVersionFiveAndPreservesSnapshotShapeAsync
- ScrollViewportProjectsToStrictOldNodeShapeAsync
- ClientCarriesReadOnlySemanticStateAsync
- SelectionRequiresAnExplicitControlPageAsync
- LegacySnapshotPreservesOldWireShapeAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/TabWireTests.cs

- RemoteExpansionUsesOrdinaryInputAsync
- ExpandedMetadataDoesNotChangeOldShapesAsync

### tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests/VirtualizationWireTests.cs

- KeyObservationAndInputHaveSeparateAuthorityAsync

### tests/Graphics/Lumyte.Graphics.Benchmark.Tests/MapWorkloadTests.cs

- ControlledStreamingUsesActualDemandRetirementAndManagerTrimAsync

### tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/RetainedSceneContentTests.cs

- ConcurrentReadersSharePublishedPages

### tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/UiRecordingTests.cs

- RecordingHasNoCreatorThreadAffinityAsync

### tests/Graphics/Lumyte.Graphics.Hosting.Tests/DeviceServiceTests.cs

- ConcurrentDisposalReturnsAnEpochLeaseOnlyOnceAsync

### tests/Graphics/Lumyte.Graphics.RenderGraph.Tests/RenderGraphProfilingTests.cs

- DelayedRecordRetainsCompileCorrelationAcrossThreadsAsync

### tests/Graphics/Lumyte.Graphics.Tests/ShaderDataLayoutCacheTests.cs

- ConcurrentAccessSharesLayouts

### tests/Graphics/Lumyte.Graphics.Tests/VulkanCompletionTests.cs

- DispatcherSharesOneSleepingWorkerAsync
- CancellationLeavesNativeWaitRunningAsync
- LossSubscriberCanDisposeDispatcherAsync
- FailedNativeWaitKeepsRetirementGuardAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/GraphicsDefinitionTests.cs

- KeyedPipelinesShareShadersAndPreserveCompilationSettingsAsync
- CustomAssetRetainsDependenciesUntilCleanupSucceedsAsync
- RebuildUsesTheSameLogicalKeyAndCpuArtifactsAsync
- ComputeAndTextureViewsRetainTheirSharedDependenciesAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/GraphicsResourceTests.cs

- LossInvalidatesGpuAndRecoveryReusesCpuAsync
- BuildOnlyGraphicsDependencyDoesNotRetainTheDeviceAsync
- StageBindingsSelectNamedGraphicsEnvironmentsAsync
- UnnamedAndNamedDefaultEnvironmentsAreIndependentAsync
- LossDuringBuildRejectsTheCandidateAsync
- FailedReleaseKeepsTheOwnerAvailableForRetryAsync
- ForeignServiceWithTheSameVersionIsRejectedWithoutLeakingItsLeaseAsync
- ScopesShareTheApplicationGraphicsEnvironmentAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/ImportLifetimeTests.cs

- FrameSnapshotIsSelectedBeforeAcquisitionAndLeasesOutliveItAsync
- PreloadedReferencesMustBelongToTheBoundManagerAsync
- PendingAcquisitionReturnsOwnershipWhenGraphClosesAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/RendererResourceTests.cs

- DefaultFactoriesShareResourcesAndOwnTheirLifetimesAsync
- ReloadAndRecoveryReuseTheFactoryReferenceAsync
- PipelineDefinitionOverridesDefaultsAndSharesSamplerAsync
- LayeredFactoryUsesTheSameResourceAndReleasesConstructionFailuresAsync
- UiFramesRetainGenerationsAcrossReloadWaitCancellationAndRecoveryAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/ResourceImageEffectTests.cs

- EmbeddedEffectsShareShadersAndRetainThemUntilGpuCompletionAsync
- LayerAndEffectsExtensionsShareStateAndPlanOwnershipAsync
- InvalidManagedEffectsEnvironmentRejectsBeforePreparingPassesAsync
- CustomShaderInputsReloadAsOnePackageAndPreserveCapturedFramesAsync
- DeviceRecoveryReusesArtifactsAndRecreatesBothModulesAsync
- FailedEffectCleanupKeepsShaderLeaseUntilRetryAsync
- FailedPreparationRetainsCleanupUntilGraphDisposalSucceedsAsync
- PartialShaderCreationReleasesTheFirstModuleAsync
- RegistrationAndCancelledBuildDoNotPreloadShadersAsync
- FailedBuildReleasesPreloadedShadersAsync

### tests/Graphics/Lumyte.Resources.Graphics.Tests/ShaderResourceTests.cs

- GraphRetainsShaderUntilCompletionAndRecoveryReusesArtifactAsync
- InvalidatedGraphInputIsRejectedBeforeRecordingAsync
- TextureImportTransfersItsLeaseToTheCompiledPlanAsync
- ShaderMemoryIsCommittedAndReclaimedAsync
- InvalidShaderReturnsAllMemoryReservationsAsync

### tests/Input/Lumyte.Input.Advanced.Tests/SettingsIntegrationTests.cs

- SavedRebindAppliesAtBoundaryAndSurvivesRestartAsync
- RebindRefusesAChangedCommittedRevisionAsync
- RebindRefusesReplacedSourceProfileAsync

### tests/Maps/Lumyte.Maps.UI.Tests/ApplicationRetirementTests.cs

- QuiescenceStaysStableWhileRetirementOwnsHeldCleanupAsync

### tests/UI/Lumyte.UI.Application.Navigation.Tests/FunctionNavigationTests.cs

- PageFunctionsNavigateWithoutForwardingContextAsync

### tests/UI/Lumyte.UI.Application.Navigation.Tests/InlineTests.cs

- InlineAritiesUseIndependentScopesAndRunOnceOnTheOwnerAsync
- ContextIsBoundBeforeConstructorAndUnavailableFromUnboundScopesAsync

### tests/UI/Lumyte.UI.Application.Navigation.Tests/LifetimeTests.cs

- PageScopesAreIndependentAcrossHistoryAndShareTheApplicationSingletonAsync
- FailedCandidatesReturnTheirScopesAndKeepTheCurrentPageAsync
- CancellationReleasesThePreparedScopeWithoutPublishingAsync
- RetirementFailureCanRetryBeforeScopeDisposalAsync
- ScopeDisposalFailureIsNotRetriedAndOtherScopesStillStopAsync
- GuardRejectionPreservesTheCurrentScopeAsync

### tests/UI/Lumyte.UI.Application.Navigation.Tests/SignalLifetimeTests.cs

- BusinessObserversAreReleasedBeforeThePageScopeAsync
- PageRetirementCancelsOwnedAsynchronousSignalsAsync

### tests/UI/Lumyte.UI.Application.Navigation.Tests/WindowTests.cs

- WindowFactoriesCreateIndependentScopesAndPreserveExistingInstancesAsync

### tests/UI/Lumyte.UI.Application.Tests/AnimationHostTests.cs

- StandardHostOwnsTimeAndCompletionDeliveryAsync
- ClockFailureStillDrainsShutdownAsync

### tests/UI/Lumyte.UI.Application.Tests/AutomationStopTests.cs

- ClickThatStopsTheApplicationCanReturnItsLeaseAsync

### tests/UI/Lumyte.UI.Application.Tests/DispatcherTests.cs

- CancellationOnlySkipsWorkThatHasNotStartedAsync
- FullQueueRejectsBeforeMutationAsync

### tests/UI/Lumyte.UI.Application.Tests/FramePacingTests.cs

- FrameDeadlinesKeepTheirOriginalPhaseAsync
- ContinuationsWakeTheOwnerAsync

### tests/UI/Lumyte.UI.Application.Tests/IntermediateTexturePoolTests.cs

- ConcurrentFrameReturnsAndRetirementReleaseEveryTargetOnceAsync

### tests/UI/Lumyte.UI.Application.Tests/LifecycleTests.cs

- DispatcherQueuesOnTheOwnerAndPreservesOrderAsync
- RetirementFailureCanBeRetriedWithoutDoubleReleaseAsync
- CleanupAdmissionAllowsOwnerWorkAfterStoppingAsync
- FinalReleaseFailureIsTerminalAndNotRetriedAsync
- FailedRootInitializationTerminatesAfterCleanupAsync
- FailingAdvanceStopsAndReleasesContentAsync

### tests/UI/Lumyte.UI.Application.Tests/ProfilingTests.cs

- ScreenshotHostReportsCancellationWithoutChangingDriverLifetimeAsync
- HeadlessFramesHaveMatchingControlEventsAsync
- DriverFailurePreservesPartialCountersAsync
- HostReconnectPreservesRunAndRestoresCallerActivityAsync

### tests/UI/Lumyte.UI.Application.Tests/RecordingHostTests.cs

- StandardHostSubmitsEveryRetainedFrameAsync

### tests/UI/Lumyte.UI.Application.Tests/TextHostTests.cs

- HostedEditorsShareOneBackendAndDrainBeforeSwitchingAsync

### tests/UI/Lumyte.UI.Application.Tests/VisualResourceTests.cs

- CompletingAnOlderCompositeDoesNotOpenThePageGateAsync
- SlideToFadeReversalUpgradesTheRetainedLayerAsync
- C1SubtreesSupportPresencePageAndSharedCaptureAsync

### tests/UI/Lumyte.UI.Application.Tests/WindowTests.cs

- SignalsOpenAndCloseWindowsWithoutExplicitRebuildAsync
- WindowAttachmentObservesChangesAfterTheInitialSnapshotAsync
- SignalFailureStopsTheApplicationAndDrainsAsync
- HostedKeysRetainStateAndReopenWithNewIdentityAsync
- UnsupportedAdditionalNativeWindowDoesNotStopPrimaryAsync
- InvalidRebuildPreservesCurrentWindowsAsync
- HostedCloseButtonUpdatesModelAndDrainsAsync
- DiagnosticsTargetsShareTheOwnerAndStopCleanlyAsync

### tests/UI/Lumyte.UI.Benchmark.Tests/AutomationApplicationBenchmarkTests.cs

- HeadlessLifecycleJoinsActualOwnerAsync

### tests/UI/Lumyte.UI.Benchmark.Tests/DesktopApplicationBenchmarkTests.cs

- ForeignOwnerCannotSelectAcceptedFrameAsync

### tests/UI/Lumyte.UI.Navigation.Tests/DiagnosticsTests.cs

- DiagnosticClickCanNavigateWithoutWaitingOnItsOwnLeaseAsync

### tests/UI/Lumyte.UI.Navigation.Tests/HistoryTests.cs

- HistoryRecreatesPagesAndOldHostsNeverFollowTheNewSurfaceAsync
- BoundedHistoryCommitsOnlySuccessfulOperationsAsync

### tests/UI/Lumyte.UI.Navigation.Tests/InputSeriesTests.cs

- PointerReleaseNeverClicksTheReplacementPageAsync
- InvalidEditorDoesNotReplaceTheCurrentPageAsync

### tests/UI/Lumyte.UI.Navigation.Tests/LifetimeTests.cs

- CleanupDispatchRemainsAvailableOnlyDuringAwaitedRetirementAsync
- TerminalDisposalFailureDoesNotDisposeTheSameLeaseTwiceAsync

### tests/UI/Lumyte.UI.Navigation.Tests/TextHostTests.cs

- NavigationDrainsThePreviousEditorBeforePublishingAsync

### tests/UI/Lumyte.UI.Navigation.Tests/TransitionTests.cs

- CanceledPreparationKeepsTheOldPageAndCleansItsOwnedLeaseAsync
- LeaveGuardPreservesPublicationAndRejectsReentryAsync
- RetirementFailureBlocksNewNavigationUntilTheSameLeaseIsRetriedAsync
- StopDuringInitialPreparationDoesNotCreateAnInitializationCycleAsync
- CompletionCallbackCanQueueTheNextPageAfterCleanupAsync

### tests/UI/Lumyte.UI.Navigation.Tests/VisualTransitionTests.cs

- HeadlessFallbackKeepsCommitAndVisualResultsSeparateAsync
- DisabledMotionDoesNotCancelNavigationAsync
- SkipBeforePreparationCompletesLeavesBusinessCommitIntactAsync

### tests/UI/Lumyte.UI.Navigation.Tests/WindowTests.cs

- EachWindowOwnsItsHistoryAndActivationLifetimeAsync

### tests/UI/Lumyte.UI.Tests/C5ScrollTests.cs

- ControllerReentryDisposalAndOwnerThread

### tests/UI/Lumyte.UI.Tests/FunctionWidgetTests.cs

- SequentialThreadMovesPreserveStateAsync

### tests/UI/Lumyte.UI.Tests/HookTests.cs

- IndependentRootsRemainIsolatedAsync

### tests/UI/Lumyte.UI.Tests/KeyboardTests.cs

- FocusAndKeysAreSurfaceLocalWithoutThreadAffinityAsync

### tests/UI/Lumyte.UI.Tests/LayoutTests.cs

- LayoutCanMoveAcrossThreadsSequentiallyAsync

### tests/UI/Lumyte.UI.Tests/PointerTests.cs

- DispatchHasNoCreatorThreadAffinityAndCapturesAreSurfaceLocalAsync

### tests/UI/Lumyte.UI.Tests/ProfilingTests.cs

- SignalsAndSequentialThreadsDoNotRetainParentsAsync

### tests/UI/Lumyte.UI.Tests/UiSurfaceTests.cs

- TreesCanBeUpdatedAcrossThreadsSequentiallyAsync

## Verification

- Source invariant: every Integration-tagged case belongs to a project advertising Integration; no duplicate Integration traits.
- git diff --check clean.
- Runtime verified with pinned .NET 10.0.403: Execution Cpu 72/72 and Integration 27/27; Resources Integration 136/136; Memory full project 27/27; Signals full project 52/52.
- Child-only direct builds initially lacked dxc/libdxcompiler. The final parent verification used the existing native paths and DXC library directory from /tmp/lumyte-lanes-env.sh; full host compilation succeeded. Final authoritative lane counts and TRX/discovery evidence are recorded in build/TASK-VERIFICATION.md.
- Workflow foreign-result tests are Integration despite cooperative fixtures: WorkflowContinuation deliberately bridges different runtimes through ThreadPool before posting to the target workflow.

## Runner proof consolidation after parent review

- Removed CpuLaneOverlapTests.FirstMethod and SecondMethod from discovery. Their
  original two-party barrier assertion is represented by the existing single
  Integration case MethodRunnerTests.SameClassMethodsOverlapAndPreserveLifetimesAsync:
  it invokes the real CPU collection runner on two private same-class probes,
  asserts both meet the barrier, and checks output, async instance disposal and
  shared-fixture creation/disposal. No environment-absent no-op remains.
- Removed the duplicate CpuMethodSchedulerOverlapsMethodsAsync alias. The runner
  project now contains seven meaningful Integration cases and no CPU cases.
- Final runtime CPU inventory: 49 projects / 3,815 passed. The 428 baseline CPU
  rows removed by old name reconcile as 422 Integration executions, four theory
  renames, and two explicitly consolidated runner rows. No dummy arithmetic
  facts or artificially preserved names count as retained behavior.
