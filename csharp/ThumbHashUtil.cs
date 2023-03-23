using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MadeByEvan.ThumbHash
{
    /// <summary>
    /// Utility class for working with <see cref="ThumbHash"/> in combination with <see cref="System.Drawing.Image"/>
    /// </summary>
    public static class ThumbHashUtil
    {
        /// <summary>
        /// Create ThumbHash byte[] from <see cref="Image"/>.
        /// The image is resized to fit inside <see cref="ThumbHash.MaxWidth"/> * <see cref="ThumbHash.MaxHeight"/> if needed,
        /// so creating an image from the resulting thumbhash can have different dimensions.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="interpolationMode"></param>
        /// <returns>ThumbHash as a byte[]</returns>
        public static byte[] ImageToThumbHash(Image image, InterpolationMode interpolationMode = InterpolationMode.Default)
        {
            if (image.Width <= ThumbHash.MaxWidth && image.Height <= ThumbHash.MaxHeight)
            {
                var bytes = BitmapToByteArray((Bitmap)image);
                return ThumbHash.RGBAToThumbHash(image.Width, image.Height, bytes);
            }

            var scale = Math.Min(ThumbHash.MaxWidth / (float)image.Width, ThumbHash.MaxHeight / (float)image.Height);
            var size = new Size((int)Math.Floor(image.Width * scale), (int)Math.Floor(image.Height * scale));
            using (var resized = new Bitmap(size.Width, size.Height))
            {
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = interpolationMode;
                    g.DrawImage(image, 0, 0, size.Width, size.Height);
                }
                var bytes = BitmapToByteArray(resized);
                return ThumbHash.RGBAToThumbHash(size.Width, size.Height, bytes);
            }
        }

        /// <summary>
        /// Create <see cref="Image"/> from ThumbHash byte[] 
        /// </summary>
        /// <param name="thumbHash"></param>
        /// <returns></returns>
        public static Image ThumbHashToImage(byte[] thumbHash)
        {
            var rgba = ThumbHash.ThumbHashToRGBA(thumbHash);
            var image = new Bitmap(rgba.Width, rgba.Height);
            int n = 0;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = Color.FromArgb(rgba.RGBA[n + 3], rgba.RGBA[n], rgba.RGBA[n + 1], rgba.RGBA[n + 2]);
                    image.SetPixel(x, y, color);
                    n += 4;
                }
            }
            return image;
        }

        private static byte[] BitmapToByteArray(Bitmap image)
        {
            byte[] result = new byte[image.Width * image.Height * 4];
            int n = 0;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = image.GetPixel(x, y);
                    result[n++] = color.R;
                    result[n++] = color.G;
                    result[n++] = color.B;
                    result[n++] = color.A;
                }
            }
            return result;
        }
    }
}
