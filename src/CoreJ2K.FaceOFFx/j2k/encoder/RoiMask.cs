#nullable enable

using System;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>An immutable image-sized binary ROI mask. Nonzero input pixels belong to the ROI.</summary>
    internal sealed class RoiMask
    {
        private readonly byte[] _pixels;
        public int Width { get; }
        public int Height { get; }
        public long PixelCount { get; }

        private RoiMask(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
            long count = 0;
            for (var index = 0; index < pixels.Length; index++)
            {
                if (pixels[index] == 0) continue;
                pixels[index] = 1;
                count++;
            }
            PixelCount = count;
        }

        public static RoiMask FromBytes(int width, int height, ReadOnlySpan<byte> pixels)
        {
            var length = ValidateDimensions(width, height);
            if (pixels.Length != length)
                throw new ArgumentException("The mask must contain exactly width times height pixels.", nameof(pixels));
            return new RoiMask(width, height, pixels.ToArray());
        }

        public static RoiMask FromRectangle(int width, int height, int x, int y, int rectangleWidth, int rectangleHeight)
        {
            var length = ValidateDimensions(width, height);
            var region = RoiRegion.Rectangle(x, y, rectangleWidth, rectangleHeight);
            region.ValidateForImage(width, height);
            var pixels = new byte[length];
            for (var row = y; row < y + rectangleHeight; row++)
                pixels.AsSpan(row * width + x, rectangleWidth).Fill(1);
            return new RoiMask(width, height, pixels);
        }

        /// <summary>Rasterizes an ellipse using pixel centers. Portions outside the image are clipped.</summary>
        public static RoiMask FromEllipse(int width, int height, double centerX, double centerY, double radiusX, double radiusY)
        {
            var length = ValidateDimensions(width, height);
            if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
                throw new ArgumentException("Ellipse coordinates must be finite.");
            if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX <= 0 || radiusY <= 0)
                throw new ArgumentException("Ellipse radii must be finite and positive.");
            var pixels = new byte[length];
            for (var y = 0; y < height; y++)
            {
                var normalizedY = (y + 0.5 - centerY) / radiusY;
                for (var x = 0; x < width; x++)
                {
                    var normalizedX = (x + 0.5 - centerX) / radiusX;
                    if (normalizedX * normalizedX + normalizedY * normalizedY <= 1)
                        pixels[y * width + x] = 1;
                }
            }
            return new RoiMask(width, height, pixels);
        }

        internal bool Contains(int x, int y) => _pixels[y * Width + x] != 0;

        private static int ValidateDimensions(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if ((long)width * height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), "The mask dimensions exceed supported buffer lengths.");
            return width * height;
        }
    }
}
