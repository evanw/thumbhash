package com.madebyevan.thumbhash;

public final class ThumbHash {
    /**
     * Encodes an RGBA image to a ThumbHash. RGB should not be premultiplied by A.
     *
     * @param w    The width of the input image. Must be ≤100px.
     * @param h    The height of the input image. Must be ≤100px.
     * @param rgba The pixels in the input image, row-by-row. Must have w*h*4 elements.
     * @return The ThumbHash as a byte array.
     */
    public static byte[] rgbaToThumbHash(int w, int h, byte[] rgba) {
        // Encoding an image larger than 100x100 is slow with no benefit
        if (w > 100 || h > 100) throw new IllegalArgumentException(w + "x" + h + " doesn't fit in 100x100");

        // Determine the average color
        float avg_r = 0, avg_g = 0, avg_b = 0, avg_a = 0;
        for (int i = 0, j = 0; i < w * h; i++, j += 4) {
            float alpha = (rgba[j + 3] & 255) / 255.0f;
            avg_r += alpha / 255.0f * (rgba[j] & 255);
            avg_g += alpha / 255.0f * (rgba[j + 1] & 255);
            avg_b += alpha / 255.0f * (rgba[j + 2] & 255);
            avg_a += alpha;
        }
        if (avg_a > 0) {
            avg_r /= avg_a;
            avg_g /= avg_a;
            avg_b /= avg_a;
        }

        boolean hasAlpha = avg_a < w * h;
        int l_limit = hasAlpha ? 5 : 7; // Use fewer luminance bits if there's alpha
        int lx = Math.max(1, Math.round((float) (l_limit * w) / (float) Math.max(w, h)));
        int ly = Math.max(1, Math.round((float) (l_limit * h) / (float) Math.max(w, h)));
        float[] l = new float[w * h]; // luminance
        float[] p = new float[w * h]; // yellow - blue
        float[] q = new float[w * h]; // red - green
        float[] a = new float[w * h]; // alpha

        // Convert the image from RGBA to LPQA (composite atop the average color)
        for (int i = 0, j = 0; i < w * h; i++, j += 4) {
            float alpha = (rgba[j + 3] & 255) / 255.0f;
            float r = avg_r * (1.0f - alpha) + alpha / 255.0f * (rgba[j] & 255);
            float g = avg_g * (1.0f - alpha) + alpha / 255.0f * (rgba[j + 1] & 255);
            float b = avg_b * (1.0f - alpha) + alpha / 255.0f * (rgba[j + 2] & 255);
            l[i] = (r + g + b) / 3.0f;
            p[i] = (r + g) / 2.0f - b;
            q[i] = r - g;
            a[i] = alpha;
        }

        // Encode using the DCT into DC (constant) and normalized AC (varying) terms
        Channel l_channel = new Channel(Math.max(3, lx), Math.max(3, ly)).encode(w, h, l);
        Channel p_channel = new Channel(3, 3).encode(w, h, p);
        Channel q_channel = new Channel(3, 3).encode(w, h, q);
        Channel a_channel = hasAlpha ? new Channel(5, 5).encode(w, h, a) : null;

        // Write the constants
        boolean isLandscape = w > h;
        int header24 = Math.round(63.0f * l_channel.dc)
                | (Math.round(31.5f + 31.5f * p_channel.dc) << 6)
                | (Math.round(31.5f + 31.5f * q_channel.dc) << 12)
                | (Math.round(31.0f * l_channel.scale) << 18)
                | (hasAlpha ? 1 << 23 : 0);
        int header16 = (isLandscape ? ly : lx)
                | (Math.round(63.0f * p_channel.scale) << 3)
                | (Math.round(63.0f * q_channel.scale) << 9)
                | (isLandscape ? 1 << 15 : 0);
        int ac_start = hasAlpha ? 6 : 5;
        int ac_count = l_channel.ac.length + p_channel.ac.length + q_channel.ac.length
                + (hasAlpha ? a_channel.ac.length : 0);
        byte[] hash = new byte[ac_start + (ac_count + 1) / 2];
        hash[0] = (byte) header24;
        hash[1] = (byte) (header24 >> 8);
        hash[2] = (byte) (header24 >> 16);
        hash[3] = (byte) header16;
        hash[4] = (byte) (header16 >> 8);
        if (hasAlpha) hash[5] = (byte) (Math.round(15.0f * a_channel.dc)
                | (Math.round(15.0f * a_channel.scale) << 4));

        // Write the varying factors
        int ac_index = 0;
        ac_index = l_channel.writeTo(hash, ac_start, ac_index);
        ac_index = p_channel.writeTo(hash, ac_start, ac_index);
        ac_index = q_channel.writeTo(hash, ac_start, ac_index);
        if (hasAlpha) a_channel.writeTo(hash, ac_start, ac_index);
        return hash;
    }

    /**
     * Decodes a ThumbHash to an RGBA image. RGB is not be premultiplied by A.
     *
     * @param hash The bytes of the ThumbHash.
     * @return The width, height, and pixels of the rendered placeholder image.
     */
    public static Image thumbHashToRGBA(byte[] hash) {
        // Read the constants
        int header24 = (hash[0] & 255) | ((hash[1] & 255) << 8) | ((hash[2] & 255) << 16);
        int header16 = (hash[3] & 255) | ((hash[4] & 255) << 8);
        float l_dc = (float) (header24 & 63) / 63.0f;
        float p_dc = (float) ((header24 >> 6) & 63) / 31.5f - 1.0f;
        float q_dc = (float) ((header24 >> 12) & 63) / 31.5f - 1.0f;
        float l_scale = (float) ((header24 >> 18) & 31) / 31.0f;
        boolean hasAlpha = (header24 >> 23) != 0;
        float p_scale = (float) ((header16 >> 3) & 63) / 63.0f;
        float q_scale = (float) ((header16 >> 9) & 63) / 63.0f;
        boolean isLandscape = (header16 >> 15) != 0;
        int lx = Math.max(3, isLandscape ? hasAlpha ? 5 : 7 : header16 & 7);
        int ly = Math.max(3, isLandscape ? header16 & 7 : hasAlpha ? 5 : 7);
        float a_dc = hasAlpha ? (float) (hash[5] & 15) / 15.0f : 1.0f;
        float a_scale = (float) ((hash[5] >> 4) & 15) / 15.0f;

        // Read the varying factors (boost saturation by 1.25x to compensate for quantization)
        int ac_start = hasAlpha ? 6 : 5;
        int ac_index = 0;
        Channel l_channel = new Channel(lx, ly);
        Channel p_channel = new Channel(3, 3);
        Channel q_channel = new Channel(3, 3);
        Channel a_channel = null;
        ac_index = l_channel.decode(hash, ac_start, ac_index, l_scale);
        ac_index = p_channel.decode(hash, ac_start, ac_index, p_scale * 1.25f);
        ac_index = q_channel.decode(hash, ac_start, ac_index, q_scale * 1.25f);
        if (hasAlpha) {
            a_channel = new Channel(5, 5);
            a_channel.decode(hash, ac_start, ac_index, a_scale);
        }
        float[] l_ac = l_channel.ac;
        float[] p_ac = p_channel.ac;
        float[] q_ac = q_channel.ac;
        float[] a_ac = hasAlpha ? a_channel.ac : null;

        // Decode using the DCT into RGB
        float ratio = thumbHashToApproximateAspectRatio(hash);
        int w = Math.round(ratio > 1.0f ? 32.0f : 32.0f * ratio);
        int h = Math.round(ratio > 1.0f ? 32.0f / ratio : 32.0f);
        byte[] rgba = new byte[w * h * 4];
        int cx_stop = Math.max(lx, hasAlpha ? 5 : 3);
        int cy_stop = Math.max(ly, hasAlpha ? 5 : 3);
        float[] fx = new float[cx_stop];
        float[] fy = new float[cy_stop];
        for (int y = 0, i = 0; y < h; y++) {
            for (int x = 0; x < w; x++, i += 4) {
                float l = l_dc, p = p_dc, q = q_dc, a = a_dc;

                // Precompute the coefficients
                for (int cx = 0; cx < cx_stop; cx++)
                    fx[cx] = (float) Math.cos(Math.PI / w * (x + 0.5f) * cx);
                for (int cy = 0; cy < cy_stop; cy++)
                    fy[cy] = (float) Math.cos(Math.PI / h * (y + 0.5f) * cy);

                // Decode L
                for (int cy = 0, j = 0; cy < ly; cy++) {
                    float fy2 = fy[cy] * 2.0f;
                    for (int cx = cy > 0 ? 0 : 1; cx * ly < lx * (ly - cy); cx++, j++)
                        l += l_ac[j] * fx[cx] * fy2;
                }

                // Decode P and Q
                for (int cy = 0, j = 0; cy < 3; cy++) {
                    float fy2 = fy[cy] * 2.0f;
                    for (int cx = cy > 0 ? 0 : 1; cx < 3 - cy; cx++, j++) {
                        float f = fx[cx] * fy2;
                        p += p_ac[j] * f;
                        q += q_ac[j] * f;
                    }
                }

                // Decode A
                if (hasAlpha)
                    for (int cy = 0, j = 0; cy < 5; cy++) {
                        float fy2 = fy[cy] * 2.0f;
                        for (int cx = cy > 0 ? 0 : 1; cx < 5 - cy; cx++, j++)
                            a += a_ac[j] * fx[cx] * fy2;
                    }

                // Convert to RGB
                float b = l - 2.0f / 3.0f * p;
                float r = (3.0f * l - b + q) / 2.0f;
                float g = r - q;
                rgba[i] = (byte) Math.max(0, Math.round(255.0f * Math.min(1, r)));
                rgba[i + 1] = (byte) Math.max(0, Math.round(255.0f * Math.min(1, g)));
                rgba[i + 2] = (byte) Math.max(0, Math.round(255.0f * Math.min(1, b)));
                rgba[i + 3] = (byte) Math.max(0, Math.round(255.0f * Math.min(1, a)));
            }
        }
        return new Image(w, h, rgba);
    }

    /**
     * Extracts the average color from a ThumbHash. RGB is not be premultiplied by A.
     *
     * @param hash The bytes of the ThumbHash.
     * @return The RGBA values for the average color. Each value ranges from 0 to 1.
     */
    public static RGBA thumbHashToAverageRGBA(byte[] hash) {
        int header = (hash[0] & 255) | ((hash[1] & 255) << 8) | ((hash[2] & 255) << 16);
        float l = (float) (header & 63) / 63.0f;
        float p = (float) ((header >> 6) & 63) / 31.5f - 1.0f;
        float q = (float) ((header >> 12) & 63) / 31.5f - 1.0f;
        boolean hasAlpha = (header >> 23) != 0;
        float a = hasAlpha ? (float) (hash[5] & 15) / 15.0f : 1.0f;
        float b = l - 2.0f / 3.0f * p;
        float r = (3.0f * l - b + q) / 2.0f;
        float g = r - q;
        return new RGBA(
                Math.max(0, Math.min(1, r)),
                Math.max(0, Math.min(1, g)),
                Math.max(0, Math.min(1, b)),
                a);
    }

    /**
     * Extracts the approximate aspect ratio of the original image.
     *
     * @param hash The bytes of the ThumbHash.
     * @return The approximate aspect ratio (i.e. width / height).
     */
    public static float thumbHashToApproximateAspectRatio(byte[] hash) {
        byte header = hash[3];
        boolean hasAlpha = (hash[2] & 0x80) != 0;
        boolean isLandscape = (hash[4] & 0x80) != 0;
        int lx = isLandscape ? hasAlpha ? 5 : 7 : header & 7;
        int ly = isLandscape ? header & 7 : hasAlpha ? 5 : 7;
        return (float) lx / (float) ly;
    }

    public static final class Image {
        public int width;
        public int height;
        public byte[] rgba;

        public Image(int width, int height, byte[] rgba) {
            this.width = width;
            this.height = height;
            this.rgba = rgba;
        }
    }

    public static final class RGBA {
        public float r;
        public float g;
        public float b;
        public float a;

        public RGBA(float r, float g, float b, float a) {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    private static final class Channel {
        int nx;
        int ny;
        float dc;
        float[] ac;
        float scale;

        Channel(int nx, int ny) {
            this.nx = nx;
            this.ny = ny;
            int n = 0;
            for (int cy = 0; cy < ny; cy++)
                for (int cx = cy > 0 ? 0 : 1; cx * ny < nx * (ny - cy); cx++)
                    n++;
            ac = new float[n];
        }

        Channel encode(int w, int h, float[] channel) {
            int n = 0;
            float[] fx = new float[w];
            for (int cy = 0; cy < ny; cy++) {
                for (int cx = 0; cx * ny < nx * (ny - cy); cx++) {
                    float f = 0;
                    for (int x = 0; x < w; x++)
                        fx[x] = (float) Math.cos(Math.PI / w * cx * (x + 0.5f));
                    for (int y = 0; y < h; y++) {
                        float fy = (float) Math.cos(Math.PI / h * cy * (y + 0.5f));
                        for (int x = 0; x < w; x++)
                            f += channel[x + y * w] * fx[x] * fy;
                    }
                    f /= w * h;
                    if (cx > 0 || cy > 0) {
                        ac[n++] = f;
                        scale = Math.max(scale, Math.abs(f));
                    } else {
                        dc = f;
                    }
                }
            }
            if (scale > 0)
                for (int i = 0; i < ac.length; i++)
                    ac[i] = 0.5f + 0.5f / scale * ac[i];
            return this;
        }

        int decode(byte[] hash, int start, int index, float scale) {
            for (int i = 0; i < ac.length; i++) {
                int data = hash[start + (index >> 1)] >> ((index & 1) << 2);
                ac[i] = ((float) (data & 15) / 7.5f - 1.0f) * scale;
                index++;
            }
            return index;
        }

        int writeTo(byte[] hash, int start, int index) {
            for (float v : ac) {
                hash[start + (index >> 1)] |= Math.round(15.0f * v) << ((index & 1) << 2);
                index++;
            }
            return index;
        }
    }
}
