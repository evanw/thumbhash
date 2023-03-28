using System;

namespace MadeByEvan.ThumbHash
{
    /// <summary>
    /// A very compact representation of a placeholder for an image. Store it inline with your data and show it while the real image is loading for a smoother loading experience. It's similar to BlurHash but with the following advantages:
    /// <list type="bullet">
    /// <item>Encodes more detail in the same space</item>
    /// <item>Much faster to encode and decode</item>
    /// <item>Also encodes the aspect ratio</item>
    /// <item>Gives more accurate colors</item>
    /// <item>Supports images with alpha</item>
    /// </list>
    /// </summary>
    public static class ThumbHash
    {
        public const int MaxWidth = 100;
        public const int MaxHeight = 100;

        /// <summary>
        /// Encodes an RGBA image to a ThumbHash. RGB should not be premultiplied by A.
        /// </summary>
        /// <param name="width">The width of the input image. Must be ≤100px.</param>
        /// <param name="height">The height of the input image. Must be ≤100px.</param>
        /// <param name="rgba">The pixels in the input image, row-by-row. Must have w*h*4 elements.</param>
        /// <returns>The ThumbHash as a byte array.</returns>
        /// <exception cref="Exception"></exception>
        public static byte[] RGBAToThumbHash(int width, int height, byte[] rgba)
        {
            // Encoding an image larger than 100x100 is slow with no benefit
            if (width > MaxWidth || height > MaxHeight)
            {
                throw new Exception($"{width}x{height} doesn't fit in {MaxWidth}x{MaxHeight}");
            }
            if (rgba.Length != width * height * 4)
            {
                throw new Exception("The length of the byte array should be Width * Height * 4");
            }

            // Determine the average color
            float avg_r = 0, avg_g = 0, avg_b = 0, avg_a = 0;
            for (int i = 0, j = 0; i < width * height; i++, j += 4)
            {
                var alpha = (rgba[j + 3] & 255) / 255.0f;
                avg_r += alpha / 255.0f * (rgba[j] & 255);
                avg_g += alpha / 255.0f * (rgba[j + 1] & 255);
                avg_b += alpha / 255.0f * (rgba[j + 2] & 255);
                avg_a += alpha;
            }

            if (avg_a > 0)
            {
                avg_r /= avg_a;
                avg_g /= avg_a;
                avg_b /= avg_a;
            }

            var hasAlpha = avg_a < width * height;
            var l_limit = hasAlpha ? 5 : 7; // Use fewer luminance bits if there's alpha
            var lx = (int)Math.Max(1, Math.Round(l_limit * width / (float)Math.Max(width, height)));
            var ly = (int)Math.Max(1, Math.Round(l_limit * height / (float)Math.Max(width, height)));
            var l = new float[width * height]; // luminance
            var p = new float[width * height]; // yellow - blue
            var q = new float[width * height]; // red - green
            var a = new float[width * height]; // alpha

            // Convert the image from RGBA to LPQA (composite atop the average color)
            for (int i = 0, j = 0; i < width * height; i++, j += 4)
            {
                var alpha = (rgba[j + 3] & 255) / 255.0f;
                var r = avg_r * (1.0f - alpha) + alpha / 255.0f * (rgba[j] & 255);
                var g = avg_g * (1.0f - alpha) + alpha / 255.0f * (rgba[j + 1] & 255);
                var b = avg_b * (1.0f - alpha) + alpha / 255.0f * (rgba[j + 2] & 255);
                l[i] = (r + g + b) / 3.0f;
                p[i] = (r + g) / 2.0f - b;
                q[i] = r - g;
                a[i] = alpha;
            }

            // Encode using the DCT into DC (constant) and normalized AC (varying) terms
            int ny = Math.Max(3, ly);
            var l_channel = new Channel(Math.Max(3, lx), ny).Encode(width, height, l);
            var p_channel = new Channel(3, 3).Encode(width, height, p);
            var q_channel = new Channel(3, 3).Encode(width, height, q);
            var a_channel = hasAlpha ? new Channel(5, 5).Encode(width, height, a) : default;

            // Write the constants
            var isLandscape = width > height;
            var header24 = (byte)Math.Round(63.0f * l_channel.dc)
                           | ((byte)Math.Round(31.5f + 31.5f * p_channel.dc) << 6)
                           | ((byte)Math.Round(31.5f + 31.5f * q_channel.dc) << 12)
                           | ((byte)Math.Round(31.0f * l_channel.scale) << 18)
                           | (hasAlpha ? 1 << 23 : 0);
            var header16 = (isLandscape ? ly : lx)
                           | ((byte)Math.Round(63.0f * p_channel.scale) << 3)
                           | ((byte)Math.Round(63.0f * q_channel.scale) << 9)
                           | (isLandscape ? 1 << 15 : 0);
            var ac_start = hasAlpha ? 6 : 5;
            var ac_count = l_channel.ac.Length + p_channel.ac.Length + q_channel.ac.Length
                           + (hasAlpha ? a_channel.ac.Length : 0);
            var hash = new byte[ac_start + (ac_count + 1) / 2];
            hash[0] = (byte)header24;
            hash[1] = (byte)(header24 >> 8);
            hash[2] = (byte)(header24 >> 16);
            hash[3] = (byte)header16;
            hash[4] = (byte)(header16 >> 8);
            if (hasAlpha)
                hash[5] = (byte)((byte)Math.Round(15.0f * a_channel.dc)
                                 | ((byte)Math.Round(15.0f * a_channel.scale) << 4));

            // Write the varying factors
            var ac_index = 0;
            ac_index = l_channel.WriteTo(hash, ac_start, ac_index);
            ac_index = p_channel.WriteTo(hash, ac_start, ac_index);
            ac_index = q_channel.WriteTo(hash, ac_start, ac_index);
            if (hasAlpha) a_channel.WriteTo(hash, ac_start, ac_index);
            return hash;
        }

        /// <summary>
        /// Decodes a ThumbHash to an RGBA image. RGB is not be premultiplied by A.
        /// </summary>
        /// <param name="hash">The bytes of the ThumbHash</param>
        /// <returns>The width, height, and pixels of the rendered placeholder image</returns>
        public static RGBAImage ThumbHashToRGBA(byte[] hash)
        {
            // Read the constants
            var header24 = (hash[0] & 255) | ((hash[1] & 255) << 8) | ((hash[2] & 255) << 16);
            var header16 = (hash[3] & 255) | ((hash[4] & 255) << 8);
            var l_dc = (header24 & 63) / 63.0f;
            var p_dc = ((header24 >> 6) & 63) / 31.5f - 1.0f;
            var q_dc = ((header24 >> 12) & 63) / 31.5f - 1.0f;
            var l_scale = ((header24 >> 18) & 31) / 31.0f;
            var hasAlpha = header24 >> 23 != 0;
            var p_scale = ((header16 >> 3) & 63) / 63.0f;
            var q_scale = ((header16 >> 9) & 63) / 63.0f;
            var isLandscape = header16 >> 15 != 0;
            var lx = Math.Max(3, isLandscape ? hasAlpha ? 5 : 7 : header16 & 7);
            var ly = Math.Max(3, isLandscape ? header16 & 7 : hasAlpha ? 5 : 7);
            var a_dc = hasAlpha ? (hash[5] & 15) / 15.0f : 1.0f;
            var a_scale = ((hash[5] >> 4) & 15) / 15.0f;

            // Read the varying factors (boost saturation by 1.25x to compensate for quantization)
            var ac_start = hasAlpha ? 6 : 5;
            var ac_index = 0;
            var l_channel = new Channel(lx, ly);
            var p_channel = new Channel(3, 3);
            var q_channel = new Channel(3, 3);
            Channel a_channel = default;
            ac_index = l_channel.Decode(hash, ac_start, ac_index, l_scale);
            ac_index = p_channel.Decode(hash, ac_start, ac_index, p_scale * 1.25f);
            ac_index = q_channel.Decode(hash, ac_start, ac_index, q_scale * 1.25f);
            if (hasAlpha)
            {
                a_channel = new Channel(5, 5);
                a_channel.Decode(hash, ac_start, ac_index, a_scale);
            }

            var l_ac = l_channel.ac;
            var p_ac = p_channel.ac;
            var q_ac = q_channel.ac;
            var a_ac = hasAlpha ? a_channel.ac : null;

            // Decode using the DCT into RGB
            var ratio = ThumbHashToApproximateAspectRatio(hash);
            var w = (int)Math.Round(ratio > 1.0f ? 32.0f : 32.0f * ratio);
            var h = (int)Math.Round(ratio > 1.0f ? 32.0f / ratio : 32.0f);
            var rgba = new byte[w * h * 4];
            var cx_stop = Math.Max(lx, hasAlpha ? 5 : 3);
            var cy_stop = Math.Max(ly, hasAlpha ? 5 : 3);
            var fx = new float[cx_stop];
            var fy = new float[cy_stop];
            for (int y = 0, i = 0; y < h; y++)
            for (var x = 0; x < w; x++, i += 4)
            {
                float l = l_dc, p = p_dc, q = q_dc, a = a_dc;

                // Precompute the coefficients
                for (var cx = 0; cx < cx_stop; cx++)
                    fx[cx] = (float)Math.Cos(Math.PI / w * (x + 0.5f) * cx);
                for (var cy = 0; cy < cy_stop; cy++)
                    fy[cy] = (float)Math.Cos(Math.PI / h * (y + 0.5f) * cy);

                // Decode L
                for (int cy = 0, j = 0; cy < ly; cy++)
                {
                    var fy2 = fy[cy] * 2.0f;
                    for (var cx = cy > 0 ? 0 : 1; cx * ly < lx * (ly - cy); cx++, j++)
                        l += l_ac[j] * fx[cx] * fy2;
                }

                // Decode P and Q
                for (int cy = 0, j = 0; cy < 3; cy++)
                {
                    var fy2 = fy[cy] * 2.0f;
                    for (var cx = cy > 0 ? 0 : 1; cx < 3 - cy; cx++, j++)
                    {
                        var f = fx[cx] * fy2;
                        p += p_ac[j] * f;
                        q += q_ac[j] * f;
                    }
                }

                // Decode A
                if (hasAlpha)
                    for (int cy = 0, j = 0; cy < 5; cy++)
                    {
                        var fy2 = fy[cy] * 2.0f;
                        for (var cx = cy > 0 ? 0 : 1; cx < 5 - cy; cx++, j++)
                            a += a_ac[j] * fx[cx] * fy2;
                    }

                // Convert to RGB
                var b = l - 2.0f / 3.0f * p;
                var r = (3.0f * l - b + q) / 2.0f;
                var g = r - q;
                rgba[i] = (byte)Math.Max(0, Math.Round(255.0f * Math.Min(1, r)));
                rgba[i + 1] = (byte)Math.Max(0, Math.Round(255.0f * Math.Min(1, g)));
                rgba[i + 2] = (byte)Math.Max(0, Math.Round(255.0f * Math.Min(1, b)));
                rgba[i + 3] = (byte)Math.Max(0, Math.Round(255.0f * Math.Min(1, a)));
            }

            return new RGBAImage(w, h, rgba);
        }

        /// <summary>
        /// Extracts the average color from a ThumbHash. RGB is not be premultiplied by A.
        /// </summary>
        /// <param name="hash">The bytes of the ThumbHash</param>
        /// <returns>The RGBA values for the average color. Each value ranges from 0 to 1.</returns>
        
        public static RGBA ThumbHashToAverageRGBA(byte[] hash)
        {
            var header = (hash[0] & 255) | ((hash[1] & 255) << 8) | ((hash[2] & 255) << 16);
            var l = (header & 63) / 63.0f;
            var p = ((header >> 6) & 63) / 31.5f - 1.0f;
            var q = ((header >> 12) & 63) / 31.5f - 1.0f;
            var hasAlpha = header >> 23 != 0;
            var a = hasAlpha ? (hash[5] & 15) / 15.0f : 1.0f;
            var b = l - 2.0f / 3.0f * p;
            var r = (3.0f * l - b + q) / 2.0f;
            var g = r - q;
            return new RGBA(
                Math.Max(0, Math.Min(1, r)),
                Math.Max(0, Math.Min(1, g)),
                Math.Max(0, Math.Min(1, b)),
                a);
        }

       
        /// <summary>
        /// Extracts the approximate aspect ratio of the original image.
        /// </summary>
        /// <param name="hash">The bytes of the ThumbHash</param>
        /// <returns>The approximate aspect ratio (i.e. width / height)</returns>
        public static float ThumbHashToApproximateAspectRatio(byte[] hash)
        {
            var header = hash[3];
            var hasAlpha = (hash[2] & 0x80) != 0;
            var isLandscape = (hash[4] & 0x80) != 0;
            var lx = isLandscape ? hasAlpha ? 5 : 7 : header & 7;
            var ly = isLandscape ? header & 7 : hasAlpha ? 5 : 7;
            return lx / (float)ly;
        }

        public struct RGBAImage
        {
            public readonly int Width;
            public readonly int Height;
            public readonly byte[] RGBA;

            public RGBAImage(int width, int height, byte[] rgba)
            {
                Width = width;
                Height = height;
                RGBA = rgba;
            }
        }

        public struct RGBA
        {
            public float R;
            public float G;
            public float B;
            public float A;

            public RGBA(float r, float g, float b, float a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }
        }

        private struct Channel
        {
            private readonly int _nx;
            private readonly int _ny;
            public float dc;
            public readonly float[] ac;
            public float scale;

            public Channel(int nx, int ny)
            {
                _nx = nx;
                _ny = ny;
                var n = 0;
                for (var cy = 0; cy < ny; cy++)
                for (var cx = cy > 0 ? 0 : 1; cx * ny < nx * (ny - cy); cx++)
                    n++;
                ac = new float[n];
                dc = 0;
                scale = 0;
            }

            public Channel Encode(int w, int h, float[] channel)
            {
                var n = 0;
                var fx = new float[w];
                for (var cy = 0; cy < _ny; cy++)
                for (var cx = 0; cx * _ny < _nx * (_ny - cy); cx++)
                {
                    float f = 0;
                    for (var x = 0; x < w; x++)
                        fx[x] = (float)Math.Cos(Math.PI / w * cx * (x + 0.5f));
                    for (var y = 0; y < h; y++)
                    {
                        var fy = (float)Math.Cos(Math.PI / h * cy * (y + 0.5f));
                        for (var x = 0; x < w; x++)
                            f += channel[x + y * w] * fx[x] * fy;
                    }

                    f /= w * h;
                    if (cx > 0 || cy > 0)
                    {
                        ac[n++] = f;
                        scale = Math.Max(scale, Math.Abs(f));
                    }
                    else
                    {
                        dc = f;
                    }
                }

                if (scale > 0)
                    for (var i = 0; i < ac.Length; i++)
                        ac[i] = 0.5f + 0.5f / scale * ac[i];
                return this;
            }

            public int Decode(byte[] hash, int start, int index, float scale)
            {
                for (var i = 0; i < ac.Length; i++)
                {
                    var data = hash[start + (index >> 1)] >> ((index & 1) << 2);
                    ac[i] = ((data & 15) / 7.5f - 1.0f) * scale;
                    index++;
                }

                return index;
            }

            public int WriteTo(byte[] hash, int start, int index)
            {
                foreach (var v in ac)
                {
                    hash[start + (index >> 1)] |= (byte)((byte)Math.Round(15.0f * v) << ((index & 1) << 2));
                    index++;
                }
                return index;
            }
        }
    }
}
