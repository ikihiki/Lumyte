using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class ImageSet : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly bool _atlas;
    private readonly IGraphicsTexture[] _textures;
    private readonly IGraphicsTextureView[] _views;
    private readonly IGraphicsSampler _sampler;
    private readonly Image2D[] _images;
    private readonly IGraphicsBuffer<byte>[] _uploads;
    private readonly HardwareGpuTimer _timer;
    private readonly uint _pitch;
    private bool _initialized;
    private bool _unknown;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;

    internal ImageSet(VulkanDevice device, bool atlas)
    {
        _device = device;
        _atlas = atlas;
        uint width = atlas ? 64u : 16u;
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb);
        uint alignment = Math.Max(layout.BytesPerRowAlignment, device.Caps.CopyBytesPerRowAlignment);
        _pitch = ((((width * 4) + alignment) - 1) / alignment) * alignment;
        if (_pitch % layout.BytesPerRowAlignment != 0 || _pitch % device.Caps.CopyBytesPerRowAlignment != 0)
        {
            throw new NotSupportedException("Image row alignment is not compatible with the fixed profile.");
        }

        _textures = Enumerable.Range(0, atlas ? 1 : 4).Select(_ => device.CreateTexture(new() { Width = width, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination })).ToArray();
        _views = _textures.Select(texture => texture.CreateView()).ToArray();
        _sampler = device.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest });
        _images = _views.Select(view => new Image2D(view, _sampler)).ToArray();
        _uploads = _textures.Select(_ => device.CreateBuffer<byte>(new() { Count = _pitch * 16, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload })).ToArray();
        _timer = new(device);
        Payloads = Enumerable.Range(0, 2).Select(state => Enumerable.Range(0, _textures.Length).Select(index => Pixels(atlas, index, state, _pitch)).ToArray()).ToArray();
    }

    internal byte[][][] Payloads { get; }

    internal uint Pitch => _pitch;

    internal double UploadHostMs { get; private set; }

    internal double? UploadGpuMs { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_unknown)
        {
            PublicRetainedApiProbe.QuarantinedObjects.Add(this);
            return;
        }

        _commands?.Dispose();
        _timer.Dispose();
        foreach (IGraphicsBuffer<byte> upload in _uploads)
        {
            upload.Dispose();
        }

        foreach (IGraphicsTextureView view in _views)
        {
            view.Dispose();
        }

        foreach (IGraphicsTexture texture in _textures)
        {
            texture.Dispose();
        }

        _sampler.Dispose();
    }

    internal static string AssetHash(int state) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(true, 0, state, 256)));

    internal static Color2D Color(int type) => type switch { 0 => new(1, 0, 0, 1), 1 => new(0, 1, 0, 1), 2 => new(0, 0, 1, 1), _ => new(1, 1, 0, 1) };

    internal static int[] Order(bool grouped) => grouped ? Enumerable.Range(0, 4).SelectMany(type => Enumerable.Range(0, 256).Where(i => i % 4 == type)).ToArray() : Enumerable.Range(0, 256).ToArray();

    internal static DrawList2D Reference(bool grouped, int state)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        foreach (int i in Order(grouped))
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool white = ((x + y + state) & 1) == 0;
                    canvas.FillRect(new(8 + ((i % 16) * 32) + (x * 4), 8 + ((i / 16) * 32) + (y * 4), 4, 4), white ? new Color2D(1, 1, 1, 1) : Color(i % 4));
                }
            }
        }

        return canvas.End();
    }

    internal static void VerifyAnalytic(byte[] pixels, int state)
    {
        if (pixels.Length != 1920 * 1080 * 4)
        {
            throw new InvalidOperationException("Invalid image target dimensions.");
        }

        for (int i = 0; i < 256; i++)
        {
            for (int sample = 0; sample < 2; sample++)
            {
                bool white = ((sample + state) & 1) == 0;
                Color2D color = white ? new(1, 1, 1, 1) : Color(i % 4);
                int x = 10 + ((i % 16) * 32) + (sample * 4), y = 10 + ((i / 16) * 32);
                int offset = ((y * 1920) + x) * 4;
                byte[] expected = [(byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), 255];
                if (!pixels.AsSpan(offset, 4).SequenceEqual(expected))
                {
                    throw new InvalidOperationException("Analytic image checker color mismatch.");
                }
            }
        }
    }

    internal DrawList2D Recording(bool grouped)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        foreach (int i in Order(grouped))
        {
            int type = i % 4;
            canvas.DrawImage(_images[_atlas ? 0 : type], new(_atlas ? type * 16 : 0, 0, 16, 16), new(8 + ((i % 16) * 32), 8 + ((i / 16) * 32), 16, 16));
        }

        return canvas.End();
    }

    internal async Task UploadAsync(int state)
    {
        if (_unknown)
        {
            throw new InvalidOperationException("Image upload ownership uncertain.");
        }

        long begin = Stopwatch.GetTimestamp();
        for (int i = 0; i < _uploads.Length; i++)
        {
            await _uploads[i].MapAsync();
            _uploads[i].CopyFrom(Payloads[state][i]);
            _uploads[i].Unmap();
        }

        _commands = _device.CreateCommandBuffer(new());
        if (_timer.Supported)
        {
            _timer.Begin(_commands);
        }

        for (int i = 0; i < _textures.Length; i++)
        {
            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _uploads[i].SliceElements(0, _uploads[i].Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            _commands.Barrier(new TextureBarrierDesc { Texture = _textures[i], Range = new(0, 1, 0, 1), BeforeState = _initialized ? TextureState.Sampled : TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = _initialized ? new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) : default, After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
            _commands.CopyBufferToTexture(new() { Buffer = _uploads[i].SliceElements(0, _uploads[i].Count), BytesPerRow = _pitch, RowsPerImage = 16 }, new() { Texture = _textures[i], Width = _textures[i].Width, Height = 16 });
            _commands.Barrier(new TextureBarrierDesc { Texture = _textures[i], Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
        }

        if (_timer.Supported)
        {
            _timer.End(_commands);
        }

        _commands.Finish();
        _unknown = true;
        _submission = _device.Queue.Submit([_commands]);
        await _submission.WaitAsync();
        if (_submission.Status != SubmissionStatus.Completed)
        {
            throw new InvalidOperationException("Image upload fence failed.");
        }

        UploadHostMs = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        UploadGpuMs = _timer.Supported ? _timer.ReadMilliseconds() : null;
        _unknown = false;
        _initialized = true;
        _commands.Dispose();
        _commands = null;
        _submission.Dispose();
        _submission = null;
    }

    private static byte[] Pixels(bool atlas, int index, int state, uint pitch)
    {
        byte[] bytes = new byte[checked((int)pitch * 16)];
        int width = atlas ? 64 : 16;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int type = atlas ? x / 16 : index;
                bool white = ((((x % 16) / 4) + (y / 4) + state) & 1) == 0;
                Color2D color = white ? new(1, 1, 1, 1) : Color(type);
                int offset = (y * checked((int)pitch)) + (x * 4);
                bytes[offset] = (byte)(color.R * 255);
                bytes[offset + 1] = (byte)(color.G * 255);
                bytes[offset + 2] = (byte)(color.B * 255);
                bytes[offset + 3] = 255;
            }
        }

        return bytes;
    }
}
