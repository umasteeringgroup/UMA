using System;
using System.IO;
using System.IO.Compression;

namespace UMA.Editors
{
    /// <summary>A decoded PNG: values 0..1, row 0 at the top, <see cref="Channels"/> values per pixel.</summary>
    public sealed class UMAPngImage
    {
        public int Width, Height, Channels, BitDepth;
        public float[] Values;

        /// <summary>The first channel (gray, or red) of every pixel, row 0 at the top.</summary>
        public float[] FirstChannel()
        {
            var result = new float[Width * Height];
            for (int i = 0; i < result.Length; i++) result[i] = Values[i * Channels];
            return result;
        }
    }

    /// <summary>
    /// Reads PNG files at their full precision. Unity's ImageConversion.LoadImage reduces 16-bit PNGs to 8 bits, which
    /// is too coarse for a height map. Supports gray, gray + alpha, RGB, RGBA (8 and 16 bits) and 8-bit palette images,
    /// not interlaced. Pure C#: no UnityEngine types.
    /// </summary>
    public static class UMAPngReader
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static UMAPngImage Read(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 8) throw new InvalidDataException("Not a PNG file.");
            for (int i = 0; i < 8; i++) if (data[i] != Signature[i]) throw new InvalidDataException("Not a PNG file.");

            int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
            byte[] palette = null;
            var compressed = new MemoryStream();
            int at = 8;
            while (at + 8 <= data.Length)
            {
                int length = ReadInt(data, at);
                string type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
                int body = at + 8;
                if (length < 0 || body + length > data.Length) throw new InvalidDataException("The PNG file is truncated.");
                if (type == "IHDR")
                {
                    width = ReadInt(data, body); height = ReadInt(data, body + 4);
                    bitDepth = data[body + 8]; colorType = data[body + 9]; interlace = data[body + 12];
                }
                else if (type == "PLTE") { palette = new byte[length]; Array.Copy(data, body, palette, 0, length); }
                else if (type == "IDAT") compressed.Write(data, body, length);
                else if (type == "IEND") break;
                at = body + length + 4; // skip the CRC
            }
            if (width <= 0 || height <= 0) throw new InvalidDataException("The PNG file has no image header.");
            if (interlace != 0) throw new InvalidDataException("Interlaced PNG files are not supported. Save the image without interlacing.");
            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new InvalidDataException("Unknown PNG color type " + colorType + ".");
            }
            bool paletted = colorType == 3;
            if (paletted ? bitDepth != 8 : bitDepth != 8 && bitDepth != 16)
                throw new InvalidDataException("PNG bit depth " + bitDepth + " is not supported. Save 8 or 16 bits per channel.");
            if (paletted && palette == null) throw new InvalidDataException("The palette PNG file has no palette.");

            int bytesPerSample = bitDepth / 8, pixelBytes = channels * bytesPerSample, rowBytes = width * pixelBytes;
            var raw = new byte[(long)height * (rowBytes + 1)];
            compressed.Position = 2; // zlib header; DeflateStream reads the raw stream after it
            using (var inflate = new DeflateStream(compressed, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int got = inflate.Read(raw, read, raw.Length - read);
                    if (got <= 0) throw new InvalidDataException("The PNG image data is truncated.");
                    read += got;
                }
            }

            var previous = new byte[rowBytes];
            var row = new byte[rowBytes];
            int outChannels = paletted ? 3 : channels;
            var values = new float[width * height * outChannels];
            float scale = bitDepth == 16 ? 1f / 65535f : 1f / 255f;
            for (int y = 0; y < height; y++)
            {
                int start = y * (rowBytes + 1);
                Unfilter(raw[start], raw, start + 1, row, previous, pixelBytes);
                int o = y * width * outChannels;
                if (paletted)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = row[x] * 3;
                        if (p + 2 >= palette.Length) throw new InvalidDataException("A PNG palette index is out of range.");
                        for (int c = 0; c < 3; c++) values[o++] = palette[p + c] / 255f;
                    }
                }
                else if (bitDepth == 16)
                    for (int k = 0; k < rowBytes; k += 2) values[o++] = ((row[k] << 8) | row[k + 1]) * scale;
                else
                    for (int k = 0; k < rowBytes; k++) values[o++] = row[k] * scale;
                var swap = previous; previous = row; row = swap;
            }
            return new UMAPngImage { Width = width, Height = height, Channels = outChannels, BitDepth = bitDepth, Values = values };
        }

        static void Unfilter(byte filter, byte[] src, int offset, byte[] row, byte[] previous, int bpp)
        {
            int n = row.Length;
            for (int k = 0; k < n; k++)
            {
                int a = k >= bpp ? row[k - bpp] : 0, b = previous[k], c = k >= bpp ? previous[k - bpp] : 0;
                int value = src[offset + k];
                switch (filter)
                {
                    case 0: break;
                    case 1: value += a; break;
                    case 2: value += b; break;
                    case 3: value += (a + b) >> 1; break;
                    case 4: value += Paeth(a, b, c); break;
                    default: throw new InvalidDataException("Unknown PNG row filter " + filter + ".");
                }
                row[k] = (byte)value;
            }
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static int ReadInt(byte[] data, int at) { return (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]; }
    }
}
