using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private sealed class QueueRecord : NativeGpuQueue
    {
        private readonly Queue queue;
        private readonly uint queueFamily;
        private readonly VkSemaphore completion;
        private readonly List<Retirement> pending = [];
        private ulong submittedValue;

        public QueueRecord(VulkanBackend owner, Queue queue, uint queueFamily)
        {
            Owner = owner;
            this.queue = queue;
            this.queueFamily = queueFamily;
            CopyMemory = (delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryInfo*, void>)
                (nint)owner.vk.GetDeviceProcAddr(owner.device, "vkCmdCopyMemoryKHR");
            CopyMemoryToImage = (delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryImageInfo*, void>)
                (nint)owner.vk.GetDeviceProcAddr(owner.device, "vkCmdCopyMemoryToImageKHR");
            CopyImageToMemory = (delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryImageInfo*, void>)
                (nint)owner.vk.GetDeviceProcAddr(owner.device, "vkCmdCopyImageToMemoryKHR");
            if (CopyMemory == null || CopyMemoryToImage == null || CopyImageToMemory == null)
            {
                throw new NotSupportedException("VK_KHR_device_address_commands copy entry points are unavailable.");
            }
            completion = owner.CreateTimeline(0);
        }

        public VulkanBackend Owner { get; }
        public readonly delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryInfo*, void> CopyMemory;
        public readonly delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryImageInfo*, void> CopyMemoryToImage;
        public readonly delegate* unmanaged<CommandBuffer, NativeCopyDeviceMemoryImageInfo*, void> CopyImageToMemory;

        public override NativeGpuCommandBuffer StartCommandRecording()
        {
            VerifyAvailable();
            ReclaimCompleted();
            CommandPool pool = CreatePool();
            try
            {
                return new CommandRecord(this, pool, AllocateCommand(pool));
            }
            catch
            {
                Owner.vk.DestroyCommandPool(Owner.device, pool, null);
                throw;
            }
        }

        public override void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuTimelinePoint signal,
            ReadOnlySpan<NativeGpuTimelinePoint> waits = default)
        {
            VerifyAvailable();
            SemaphoreRecord signalSemaphore = Owner.RequireSemaphore(signal.Semaphore, nameof(signal));
            SemaphoreSubmitInfo[] waitInfos = new SemaphoreSubmitInfo[waits.Length];
            for (int index = 0; index < waits.Length; index++)
            {
                SemaphoreRecord wait = Owner.RequireSemaphore(waits[index].Semaphore, nameof(waits));
                waitInfos[index] = new()
                {
                    SType = StructureType.SemaphoreSubmitInfo, Semaphore = wait.Semaphore,
                    Value = waits[index].Value, StageMask = PipelineStageFlags2.AllCommandsBit,
                };
            }
            ReclaimCompleted();
            CommandRecord[] recordings = new CommandRecord[commands.Length];
            HashSet<CommandRecord> unique = [];
            for (int index = 0; index < commands.Length; index++)
            {
                if (commands[index] is not CommandRecord recording || !ReferenceEquals(recording.Queue, this))
                {
                    throw new ArgumentException("A recording belongs to another queue or backend.", nameof(commands));
                }
                recording.VerifyRecording();
                if (!unique.Add(recording)) { throw new ArgumentException("A recording occurs more than once in the batch.", nameof(commands)); }
                recordings[index] = recording;
            }

            ulong nextValue = checked(submittedValue + 1);
            pending.EnsureCapacity(checked(pending.Count + 1));
            HashSet<TextureRecord> initialized = [];
            List<CommandBufferSubmitInfo> nativeCommands = [];
            CommandPool initializationPool = default;
            try
            {
                // Retain the caller's command order. Initialization can write image metadata and must
                // follow any earlier alias dependency, even within this same recording.
                foreach (CommandRecord recording in recordings)
                {
                    foreach (CommandSegment segment in recording.Segments)
                    {
                        TextureRecord? texture = segment.InitializationCandidate;
                        if (texture is not null && texture.RequiresGeneralInitialization && initialized.Add(texture))
                        {
                            ObjectDisposedException.ThrowIf(texture.Destroyed, texture);
                            if (initializationPool.Handle == 0) { initializationPool = CreatePool(); }
                            CommandBuffer initialize = AllocateCommand(initializationPool);
                            var description = texture.Description;
                            ImageAspectFlags aspects = description.Format switch
                            {
                                GpuFormat.D32Float => ImageAspectFlags.DepthBit,
                                GpuFormat.Depth24PlusStencil8 => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
                                _ => ImageAspectFlags.ColorBit,
                            };
                            Owner.RecordDiscard(initialize, texture, new(aspects, 0, description.MipCount, 0, description.LayerCount));
                            CheckResult(Owner.vk.EndCommandBuffer(initialize), "vkEndCommandBuffer (image initialization)");
                            nativeCommands.Add(SubmitCommand(initialize));
                        }
                        nativeCommands.Add(SubmitCommand(segment.Command));
                    }
                }
                CommandBufferSubmitInfo[] commandInfos = nativeCommands.ToArray();
                Retirement retirement = new(nextValue, recordings, initializationPool);
                foreach (CommandRecord recording in recordings) { recording.End(); }

                SemaphoreSubmitInfo* signals = stackalloc SemaphoreSubmitInfo[2];
                signals[0] = new()
                {
                    SType = StructureType.SemaphoreSubmitInfo, Semaphore = completion,
                    Value = nextValue, StageMask = PipelineStageFlags2.AllCommandsBit,
                };
                signals[1] = new()
                {
                    SType = StructureType.SemaphoreSubmitInfo, Semaphore = signalSemaphore.Semaphore,
                    Value = signal.Value, StageMask = PipelineStageFlags2.AllCommandsBit,
                };
                fixed (CommandBufferSubmitInfo* pointer = commandInfos)
                fixed (SemaphoreSubmitInfo* waitPointer = waitInfos)
                {
                    // Signals in one VkSubmitInfo2 are unordered. A second signal-only native batch
                    // makes caller completion include the internal signal, including at backend disposal.
                    SubmitInfo2* submits = stackalloc SubmitInfo2[2];
                    submits[0] = new()
                    {
                        SType = StructureType.SubmitInfo2, CommandBufferInfoCount = checked((uint)commandInfos.Length),
                        PCommandBufferInfos = pointer, SignalSemaphoreInfoCount = 1, PSignalSemaphoreInfos = &signals[0],
                        WaitSemaphoreInfoCount = checked((uint)waitInfos.Length), PWaitSemaphoreInfos = waitPointer,
                    };
                    submits[1] = new()
                    {
                        SType = StructureType.SubmitInfo2, SignalSemaphoreInfoCount = 1, PSignalSemaphoreInfos = &signals[1],
                    };
                    CheckResult(Owner.vk.QueueSubmit2(queue, 2, submits, default), "vkQueueSubmit2");
                }

                // No allocation or native operation may fail between acceptance and retirement ownership.
                pending.Add(retirement);
                initializationPool = default;
                submittedValue = nextValue;
                foreach (TextureRecord texture in initialized) { texture.RequiresGeneralInitialization = false; }
                foreach (CommandRecord recording in recordings) { recording.MarkSubmitted(); }
            }
            catch
            {
                foreach (CommandRecord recording in recordings) { recording.Reject(); }
                if (initializationPool.Handle != 0) { Owner.vk.DestroyCommandPool(Owner.device, initializationPool, null); }
                throw;
            }
        }

        private static CommandBufferSubmitInfo SubmitCommand(CommandBuffer command) => new()
        {
            SType = StructureType.CommandBufferSubmitInfo, CommandBuffer = command,
        };

        private CommandPool CreatePool()
        {
            CommandPoolCreateInfo info = new()
            {
                SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = queueFamily, Flags = CommandPoolCreateFlags.TransientBit,
            };
            CheckResult(Owner.vk.CreateCommandPool(Owner.device, &info, null, out CommandPool pool), "vkCreateCommandPool");
            return pool;
        }

        public CommandBuffer AllocateCommand(CommandPool pool)
        {
            CommandBufferAllocateInfo allocate = new()
            {
                SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool,
                Level = CommandBufferLevel.Primary, CommandBufferCount = 1,
            };
            CheckResult(Owner.vk.AllocateCommandBuffers(Owner.device, &allocate, out CommandBuffer command), "vkAllocateCommandBuffers");
            CommandBufferBeginInfo begin = new()
            {
                SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            CheckResult(Owner.vk.BeginCommandBuffer(command, &begin), "vkBeginCommandBuffer");
            return command;
        }

        private void ReclaimCompleted()
        {
            if (pending.Count == 0) { return; }
            CheckResult(Owner.vk.GetSemaphoreCounterValue(Owner.device, completion, out ulong completed), "vkGetSemaphoreCounterValue (command memory)");
            int count = 0;
            foreach (Retirement retirement in pending)
            {
                if (retirement.Value > completed) { break; }
                foreach (CommandRecord recording in retirement.Recordings) { recording.ReleaseNative(); }
                if (retirement.InitializationPool.Handle != 0) { Owner.vk.DestroyCommandPool(Owner.device, retirement.InitializationPool, null); }
                count++;
            }
            pending.RemoveRange(0, count);
        }

        public void VerifyAvailable() => Owner.VerifyAvailable();

        public void CheckResult(Result result, string operation)
        {
            Owner.CheckDeviceResult(result, operation);
        }

        public void ReleaseInternalObjects()
        {
            // Backend disposal has a caller completion precondition. No WaitIdle or application-resource cleanup.
            foreach (Retirement retirement in pending)
            {
                foreach (CommandRecord recording in retirement.Recordings) { recording.ReleaseNative(); }
                if (retirement.InitializationPool.Handle != 0) { Owner.vk.DestroyCommandPool(Owner.device, retirement.InitializationPool, null); }
            }
            pending.Clear();
            Owner.vk.DestroySemaphore(Owner.device, completion, null);
        }

        private sealed record Retirement(ulong Value, CommandRecord[] Recordings, CommandPool InitializationPool);
    }


}
