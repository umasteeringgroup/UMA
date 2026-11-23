using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// Compresses generated textures on the GPU. A compute shader encodes every 4x4 block of the
    /// source; the blocks then reach a compressed Texture2D either by a GPU copy (no CPU memory
    /// involved) or, where the device cannot do that copy, by reading the blocks back and
    /// uploading them. The readback is a quarter of the size of the uncompressed texture.
    /// Devices without compute shaders (WebGL among them) report <see cref="TransferMode.Unavailable"/>.
    /// </summary>
    public static class UMAGpuTextureCompressor
    {
        public enum TransferMode
        {
            /// <summary>GPU compression cannot be used; generation keeps its uncompressed output.</summary>
            Unavailable,
            /// <summary>Blocks are read back from the GPU and uploaded as compressed data.</summary>
            Readback,
            /// <summary>Blocks are copied on the GPU into the compressed texture.</summary>
            DirectCopy
        }

        private const string BCShaderPath = "Shader/UMATextureCompressBC";
        private const string ETC2ShaderPath = "Shader/UMATextureCompressETC2";
        private const string BufferKeyword = "UMA_BLOCKS_TO_BUFFER";

        private static readonly int SourceId = Shader.PropertyToID("_Source");
        private static readonly int SourceMipId = Shader.PropertyToID("_SourceMip");
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int BlockCountId = Shader.PropertyToID("_BlockCount");
        private static readonly int Blocks16Id = Shader.PropertyToID("_Blocks16");
        private static readonly int Blocks8Id = Shader.PropertyToID("_Blocks8");
        private static readonly int BlockBufferId = Shader.PropertyToID("_BlockBuffer");
        private static readonly int BlockBufferOffsetId = Shader.PropertyToID("_BlockBufferOffset");

        private static bool resolved;
        private static TransferMode deviceMode;
        private static GraphicsFormat deviceFormat;
        private static ComputeShader bcShader;
        private static ComputeShader etc2Shader;
        private static readonly int[] sizeScratch = new int[2];

        /// <summary>
        /// Replaces the transfer mode found for this device. Set Readback for a device whose GPU
        /// copy misbehaves, or Unavailable to keep GPU compression off there. Null (the default)
        /// uses what the device supports. A mode the device cannot do must not be forced.
        /// </summary>
        public static TransferMode? ModeOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            resolved = false;
            deviceMode = TransferMode.Unavailable;
            deviceFormat = GraphicsFormat.None;
            bcShader = null;
            etc2Shader = null;
            ModeOverride = null;
        }

        /// <summary>How this device moves compressed blocks into a texture.</summary>
        public static TransferMode Mode
        {
            get
            {
                Resolve();
                return ModeOverride ?? deviceMode;
            }
        }

        /// <summary>The alpha-capable compressed format this device uses, or None.</summary>
        public static GraphicsFormat Format
        {
            get
            {
                Resolve();
                return deviceFormat;
            }
        }

        /// <summary>True when generated textures can be compressed on this device.</summary>
        public static bool IsAvailable => Mode != TransferMode.Unavailable && Format != GraphicsFormat.None;

        /// <summary>
        /// The compressed format a generated channel gets, or None when the channel keeps its
        /// current output: the switch is off, the device cannot compress on the GPU, or the
        /// channel is not an 8-bit RGBA texture.
        /// </summary>
        public static GraphicsFormat ResolveFormat(UMAGeneratorBase generator, RenderTextureFormat channelFormat)
        {
            if (generator == null || !generator.forceGPUTextureCompression) return GraphicsFormat.None;
            if (channelFormat != RenderTextureFormat.ARGB32 && channelFormat != RenderTextureFormat.Default) return GraphicsFormat.None;
            return IsAvailable ? Format : GraphicsFormat.None;
        }

        /// <summary>
        /// True when the blocks are copied on the GPU. A texture that must stay readable needs
        /// its compressed data on the CPU, so it takes the readback route instead.
        /// </summary>
        public static bool UsesDirectCopy(bool keepReadable) => Mode == TransferMode.DirectCopy && !keepReadable;

        /// <summary>Rounds a texture dimension up to whole 4x4 blocks.</summary>
        public static int AlignToBlocks(int size) => (Mathf.Max(1, size) + 3) & ~3;

        /// <summary>True for the formats this class can encode.</summary>
        public static bool IsEncodable(GraphicsFormat format) => BlockUInts(format) != 0;

        private static int BlockUInts(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.RGBA_DXT1_UNorm:
                case GraphicsFormat.RGB_ETC2_UNorm:
                    return 2;
                case GraphicsFormat.RGBA_DXT5_UNorm:
                case GraphicsFormat.RGBA_ETC2_UNorm:
                    return 4;
                default:
                    return 0;
            }
        }

        private static bool IsBC(GraphicsFormat format) =>
            format == GraphicsFormat.RGBA_DXT1_UNorm || format == GraphicsFormat.RGBA_DXT5_UNorm;

        private static string KernelName(GraphicsFormat format)
        {
            switch (format)
            {
                case GraphicsFormat.RGBA_DXT1_UNorm: return "CompressBC1";
                case GraphicsFormat.RGBA_DXT5_UNorm: return "CompressBC3";
                case GraphicsFormat.RGB_ETC2_UNorm: return "CompressETC2RGB";
                case GraphicsFormat.RGBA_ETC2_UNorm: return "CompressETC2RGBA";
                default: return null;
            }
        }

        private static ComputeShader ShaderFor(GraphicsFormat format)
        {
            if (IsBC(format))
            {
                if (bcShader == null) bcShader = Resources.Load<ComputeShader>(BCShaderPath);
                return bcShader;
            }
            if (etc2Shader == null) etc2Shader = Resources.Load<ComputeShader>(ETC2ShaderPath);
            return etc2Shader;
        }

        private static bool TryGetKernel(GraphicsFormat format, out ComputeShader shader, out int kernel)
        {
            kernel = -1;
            shader = BlockUInts(format) != 0 ? ShaderFor(format) : null;
            string name = KernelName(format);
            if (shader == null || name == null || !shader.HasKernel(name)) return false;
            kernel = shader.FindKernel(name);
            return shader.IsSupported(kernel);
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            deviceMode = TransferMode.Unavailable;
            deviceFormat = GraphicsFormat.None;
            if (!SystemInfo.supportsComputeShaders) return;

            GraphicsFormat format = GraphicsFormat.None;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.RGBA_DXT5_UNorm, GraphicsFormatUsage.Sample) &&
                TryGetKernel(GraphicsFormat.RGBA_DXT5_UNorm, out _, out _))
            {
                format = GraphicsFormat.RGBA_DXT5_UNorm;
            }
            else if (SystemInfo.IsFormatSupported(GraphicsFormat.RGBA_ETC2_UNorm, GraphicsFormatUsage.Sample) &&
                TryGetKernel(GraphicsFormat.RGBA_ETC2_UNorm, out _, out _))
            {
                format = GraphicsFormat.RGBA_ETC2_UNorm;
            }
            if (format == GraphicsFormat.None) return;

            const CopyTextureSupport neededCopy = CopyTextureSupport.Basic | CopyTextureSupport.RTToTexture;
            // The OpenGL ES kernels only write to the buffer (see UMATextureCompressCommon.hlsl).
            bool mayCopy = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3 &&
                (SystemInfo.copyTextureSupport & neededCopy) == neededCopy &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_UInt, GraphicsFormatUsage.LoadStore);
            if (mayCopy && ProbeDirectCopy(format))
            {
                deviceMode = TransferMode.DirectCopy;
            }
            else if (SystemInfo.supportsAsyncGPUReadback)
            {
                deviceMode = TransferMode.Readback;
            }
            else
            {
                return;
            }
            deviceFormat = format;
        }

        // Whether a device copies integer blocks into a compressed texture is not something
        // Unity reports, so compress four flat blocks once and look at the result. Flat blocks
        // decode to their own colour in every format, to within the endpoint precision.
        private static bool ProbeDirectCopy(GraphicsFormat format)
        {
            const int size = 8;
            const int tolerance = 12;
            Texture2D source = null;
            Texture2D compressed = null;
            Texture2D decoded = null;
            RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int block = (y / 4) * 2 + x / 4;
                        pixels[y * size + x] = new Color32((byte)(40 + block * 60), (byte)(220 - block * 50), (byte)(90 + block * 30), (byte)(255 - block * 70));
                    }
                source = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
                source.SetPixels32(pixels);
                source.Apply(false, false);

                compressed = CompressImmediate(source, format, false);
                if (compressed == null) return false;
                compressed.filterMode = FilterMode.Point;

                target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(compressed, target);
                RenderTexture.active = target;
                decoded = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                decoded.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                var result = decoded.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (Mathf.Abs(result[i].r - pixels[i].r) > tolerance || Mathf.Abs(result[i].g - pixels[i].g) > tolerance ||
                        Mathf.Abs(result[i].b - pixels[i].b) > tolerance || Mathf.Abs(result[i].a - pixels[i].a) > tolerance) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                UMAUtils.DestroySceneObject(source);
                UMAUtils.DestroySceneObject(compressed);
                UMAUtils.DestroySceneObject(decoded);
            }
        }

        private static void ValidateSource(Texture source, GraphicsFormat format)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (BlockUInts(format) == 0) throw new ArgumentException("Unsupported GPU compression format: " + format, nameof(format));
            if ((source.width & 3) != 0 || (source.height & 3) != 0)
                throw new ArgumentException("GPU compression needs a width and height that are multiples of 4, got " + source.width + "x" + source.height + ".", nameof(source));
        }

        private static int MipCount(Texture source, bool mipmaps)
        {
            if (!mipmaps) return 1;
            int full = Mathf.FloorToInt(Mathf.Log(Mathf.Max(source.width, source.height), 2)) + 1;
            return Mathf.Clamp(source.mipmapCount, 1, full);
        }

        private static Texture2D CreateTarget(Texture source, GraphicsFormat format, int mipCount)
        {
            // No initial pixel upload: the texture is filled by the copy or by the block data.
            var flags = TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
            var texture = mipCount > 1
                ? new Texture2D(source.width, source.height, format, mipCount, flags | TextureCreationFlags.MipChain)
                : new Texture2D(source.width, source.height, format, flags);
            texture.name = source.name + " | GPU compressed";
            texture.wrapMode = source.wrapMode;
            texture.filterMode = source.filterMode;
            texture.anisoLevel = source.anisoLevel;
            texture.mipMapBias = source.mipMapBias;
            return texture;
        }

        private static void SetMipParameters(ComputeShader shader, int kernel, Texture source, int mip, out int blocksX, out int blocksY)
        {
            int width = Mathf.Max(1, source.width >> mip);
            int height = Mathf.Max(1, source.height >> mip);
            blocksX = (width + 3) / 4;
            blocksY = (height + 3) / 4;
            shader.SetTexture(kernel, SourceId, source);
            shader.SetInt(SourceMipId, mip);
            sizeScratch[0] = width; sizeScratch[1] = height;
            shader.SetInts(SourceSizeId, sizeScratch);
            sizeScratch[0] = blocksX; sizeScratch[1] = blocksY;
            shader.SetInts(BlockCountId, sizeScratch);
        }

        /// <summary>
        /// Compresses <paramref name="source"/> entirely on the GPU and returns a texture with no
        /// CPU copy. The call does not wait for the GPU. With <paramref name="mipmaps"/>, the
        /// source's own mip levels are compressed, so they must be up to date.
        /// </summary>
        public static Texture2D CompressImmediate(Texture source, GraphicsFormat format, bool mipmaps)
        {
            ValidateSource(source, format);
            if (!TryGetKernel(format, out var shader, out int kernel)) return null;

            int blockUInts = BlockUInts(format);
            GraphicsFormat blockFormat = blockUInts == 4 ? GraphicsFormat.R32G32B32A32_UInt : GraphicsFormat.R32G32_UInt;
            int mipCount = MipCount(source, mipmaps);
            Texture2D target = CreateTarget(source, format, mipCount);
            target.Apply(false, true);

            shader.DisableKeyword(BufferKeyword);
            for (int mip = 0; mip < mipCount; mip++)
            {
                SetMipParameters(shader, kernel, source, mip, out int blocksX, out int blocksY);
                var descriptor = new RenderTextureDescriptor(blocksX, blocksY, blockFormat, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                RenderTexture blocks = RenderTexture.GetTemporary(descriptor);
                try
                {
                    if (!blocks.IsCreated()) blocks.Create();
                    shader.SetTexture(kernel, blockUInts == 4 ? Blocks16Id : Blocks8Id, blocks);
                    shader.Dispatch(kernel, (blocksX + 7) / 8, (blocksY + 7) / 8, 1);
                    // One texel of the block texture is one block of the mip level.
                    Graphics.CopyTexture(blocks, 0, 0, target, 0, mip);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(blocks);
                }
            }
            return target;
        }

        /// <summary>
        /// Encodes one mip level and returns its compressed data as it is stored in a texture of
        /// that format, four bytes to an element. Waits for the GPU. Works for every encodable
        /// format, including ones this device cannot sample, which is what lets the mobile
        /// encoders be checked on a desktop.
        /// </summary>
        public static uint[] EncodeBlocks(Texture source, GraphicsFormat format, int mip = 0)
        {
            ValidateSource(source, format);
            if (!TryGetKernel(format, out var shader, out int kernel)) return null;
            mip = Mathf.Clamp(mip, 0, source.mipmapCount - 1);
            int blocksX = (Mathf.Max(1, source.width >> mip) + 3) / 4;
            int blocksY = (Mathf.Max(1, source.height >> mip) + 3) / 4;
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, blocksX * blocksY * BlockUInts(format), sizeof(uint));
            try
            {
                shader.EnableKeyword(BufferKeyword);
                shader.SetBuffer(kernel, BlockBufferId, buffer);
                SetMipParameters(shader, kernel, source, mip, out _, out _);
                shader.SetInt(BlockBufferOffsetId, 0);
                shader.Dispatch(kernel, (blocksX + 7) / 8, (blocksY + 7) / 8, 1);
                var request = AsyncGPUReadback.Request(buffer);
                request.WaitForCompletion();
                return request.hasError ? null : request.GetData<uint>().ToArray();
            }
            finally
            {
                shader.DisableKeyword(BufferKeyword);
                buffer.Release();
            }
        }

        /// <summary>
        /// A compression whose blocks are read back from the GPU. When it is done, <see cref="TakeTexture"/>
        /// hands over the compressed texture with its data set but not yet uploaded: the caller
        /// uploads it with Apply and decides there whether the CPU copy stays.
        /// </summary>
        public sealed class ReadbackJob : IDisposable
        {
            private GraphicsBuffer buffer;
            private AsyncGPUReadbackRequest request;
            private bool requested;
            private Texture2D texture;
            private Action<ReadbackJob> completed;
            private readonly int width;
            private readonly int height;
            private readonly int mipCount;
            private readonly int blockUInts;
            private readonly GraphicsFormat format;
            private readonly string name;
            private readonly TextureWrapMode wrapMode;
            private readonly FilterMode filterMode;
            private readonly int anisoLevel;
            private readonly float mipMapBias;

            public bool Done { get; private set; }
            public bool Failed { get; private set; }

            internal ReadbackJob(Texture source, GraphicsFormat format, int mipCount, Action<ReadbackJob> completed)
            {
                this.format = format;
                this.mipCount = mipCount;
                this.completed = completed;
                width = source.width;
                height = source.height;
                blockUInts = BlockUInts(format);
                name = source.name + " | GPU compressed";
                wrapMode = source.wrapMode;
                filterMode = source.filterMode;
                anisoLevel = source.anisoLevel;
                mipMapBias = source.mipMapBias;
            }

            private int MipUInts(int mip)
            {
                int blocksX = (Mathf.Max(1, width >> mip) + 3) / 4;
                int blocksY = (Mathf.Max(1, height >> mip) + 3) / 4;
                return blocksX * blocksY * blockUInts;
            }

            internal void Start(Texture source, ComputeShader shader, int kernel)
            {
                int total = 0;
                for (int mip = 0; mip < mipCount; mip++) total += MipUInts(mip);
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, sizeof(uint));
                try
                {
                    shader.EnableKeyword(BufferKeyword);
                    shader.SetBuffer(kernel, BlockBufferId, buffer);
                    int offset = 0;
                    for (int mip = 0; mip < mipCount; mip++)
                    {
                        SetMipParameters(shader, kernel, source, mip, out int blocksX, out int blocksY);
                        shader.SetInt(BlockBufferOffsetId, offset);
                        shader.Dispatch(kernel, (blocksX + 7) / 8, (blocksY + 7) / 8, 1);
                        offset += MipUInts(mip);
                    }
                    request = AsyncGPUReadback.Request(buffer, OnReadback);
                    requested = true;
                }
                catch
                {
                    Fail();
                    throw;
                }
                finally
                {
                    shader.DisableKeyword(BufferKeyword);
                }
            }

            private void OnReadback(AsyncGPUReadbackRequest finished)
            {
                Finish(finished);
            }

            private void Finish(AsyncGPUReadbackRequest finished)
            {
                if (Done) return;
                try
                {
                    if (finished.hasError)
                    {
                        Fail();
                    }
                    else
                    {
                        NativeArray<uint> data = finished.GetData<uint>();
                        var flags = TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate;
                        texture = mipCount > 1
                            ? new Texture2D(width, height, format, mipCount, flags | TextureCreationFlags.MipChain)
                            : new Texture2D(width, height, format, flags);
                        texture.name = name;
                        texture.wrapMode = wrapMode;
                        texture.filterMode = filterMode;
                        texture.anisoLevel = anisoLevel;
                        texture.mipMapBias = mipMapBias;
                        int offset = 0;
                        for (int mip = 0; mip < mipCount; mip++)
                        {
                            int count = MipUInts(mip);
                            texture.SetPixelData(data.GetSubArray(offset, count), mip);
                            offset += count;
                        }
                        Done = true;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Fail();
                }
                finally
                {
                    ReleaseBuffer();
                }
                var callback = completed;
                completed = null;
                callback?.Invoke(this);
            }

            private void Fail()
            {
                Done = true;
                Failed = true;
                if (texture != null)
                {
                    UMAUtils.DestroySceneObject(texture);
                    texture = null;
                }
                ReleaseBuffer();
            }

            private void ReleaseBuffer()
            {
                if (buffer == null) return;
                buffer.Release();
                buffer = null;
            }

            /// <summary>Blocks until the GPU has delivered the blocks.</summary>
            public void WaitForCompletion()
            {
                if (Done || !requested) return;
                request.WaitForCompletion();
                Finish(request);
            }

            /// <summary>Hands the compressed texture to the caller, who then owns it. Null after a failure.</summary>
            public Texture2D TakeTexture()
            {
                var result = texture;
                texture = null;
                return result;
            }

            /// <summary>Drops the result. A request still in flight is waited for, so its buffer can be released.</summary>
            public void Dispose()
            {
                completed = null;
                if (!Done && requested) request.WaitForCompletion();
                Done = true;
                if (texture != null)
                {
                    UMAUtils.DestroySceneObject(texture);
                    texture = null;
                }
                ReleaseBuffer();
            }
        }

        /// <summary>
        /// Starts a compression whose blocks are read back from the GPU. <paramref name="completed"/>
        /// runs once, on the main thread, when the job is done or has failed. Returns null when the
        /// format cannot be encoded on this device.
        /// </summary>
        public static ReadbackJob CompressWithReadback(Texture source, GraphicsFormat format, bool mipmaps, Action<ReadbackJob> completed = null)
        {
            ValidateSource(source, format);
            if (!TryGetKernel(format, out var shader, out int kernel)) return null;
            var job = new ReadbackJob(source, format, MipCount(source, mipmaps), completed);
            job.Start(source, shader, kernel);
            return job;
        }

        /// <summary>
        /// Compresses and waits for the result. The returned texture is uploaded; it keeps its
        /// compressed data on the CPU only when <paramref name="keepReadable"/> is set.
        /// </summary>
        public static Texture2D CompressBlocking(Texture source, GraphicsFormat format, bool mipmaps, bool keepReadable)
        {
            if (UsesDirectCopy(keepReadable)) return CompressImmediate(source, format, mipmaps);
            using (ReadbackJob job = CompressWithReadback(source, format, mipmaps))
            {
                if (job == null) return null;
                job.WaitForCompletion();
                Texture2D texture = job.TakeTexture();
                if (texture == null) return null;
                GeneratorTextureQuality.FinishConvertedTexture(texture, false, GeneratorRuntimeCompression.Disabled, keepReadable);
                return texture;
            }
        }
    }
}
