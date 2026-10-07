#nullable enable

using System;
using CoreJ2K.FaceOFFx.j2k.roi.encoder;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>An immutable ROI region in source-image pixel coordinates, applied to every component.</summary>
    internal sealed class RoiRegion
    {
        private enum Shape { Rectangle, Circle, Mask }
        private readonly Shape _shape;
        private readonly int _x;
        private readonly int _y;
        private readonly int _width;
        private readonly int _height;
        private readonly int _radius;
        private readonly RoiMask? _mask;

        private RoiRegion(Shape shape, int x, int y, int width, int height, int radius, RoiMask? mask)
        {
            _shape = shape;
            _x = x;
            _y = y;
            _width = width;
            _height = height;
            _radius = radius;
            _mask = mask;
        }

        public static RoiRegion Rectangle(int x, int y, int width, int height)
        {
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if ((long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
                throw new ArgumentException("The rectangle coordinates exceed supported image bounds.");
            return new RoiRegion(Shape.Rectangle, x, y, width, height, 0, null);
        }

        /// <summary>Matches legacy circular ROI rasterization: squared distance is strictly less than radius squared.</summary>
        public static RoiRegion Circle(int centerX, int centerY, int radius)
        {
            if (centerX < 0) throw new ArgumentOutOfRangeException(nameof(centerX));
            if (centerY < 0) throw new ArgumentOutOfRangeException(nameof(centerY));
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if ((long)centerX + radius > int.MaxValue || (long)centerY + radius > int.MaxValue)
                throw new ArgumentException("The circle coordinates exceed supported image bounds.");
            return new RoiRegion(Shape.Circle, centerX, centerY, 0, 0, radius, null);
        }

        public static RoiRegion FromMask(RoiMask mask)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            return new RoiRegion(Shape.Mask, 0, 0, mask.Width, mask.Height, 0, mask);
        }

        internal void ValidateForImage(int width, int height)
        {
            if (_shape == Shape.Mask)
            {
                if (_width != width || _height != height)
                    throw new ArgumentException("Input image and ROI mask must have the same size.");
            }
            else if (_shape == Shape.Rectangle)
            {
                if ((long)_x + _width > width || (long)_y + _height > height)
                    throw new ArgumentException("The ROI rectangle must fit inside the source image.");
            }
            else if ((long)_x - _radius < 0 || (long)_y - _radius < 0 ||
                     (long)_x + _radius > width || (long)_y + _radius > height)
                throw new ArgumentException("The ROI circle must fit inside the source image.");
        }

        internal bool Contains(int x, int y)
        {
            if (_shape == Shape.Mask) return _mask!.Contains(x, y);
            if (_shape == Shape.Rectangle)
                return x >= _x && y >= _y && x < _x + _width && y < _y + _height;
            var dx = (long)x - _x;
            var dy = (long)y - _y;
            return dx * dx + dy * dy < (long)_radius * _radius;
        }

        internal long? SimplePixelCount => _shape switch
        {
            Shape.Rectangle => (long)_width * _height,
            Shape.Mask => _mask!.PixelCount,
            _ => null
        };

        internal ROI ToLegacy(int component, int originX, int originY)
        {
            if (_shape == Shape.Mask) return new ROI(component, _mask!);
            var x = checked(_x + originX);
            var y = checked(_y + originY);
            return _shape == Shape.Rectangle ? new ROI(component, x, y, _width, _height) : new ROI(component, x, y, _radius);
        }
    }
}
