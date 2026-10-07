#nullable enable
using System;
using CoreJ2K.FaceOFFx.j2k.image;
using CoreJ2K.FaceOFFx.j2k.quantization.quantizer;
using CoreJ2K.FaceOFFx.j2k.util;
namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    internal sealed record EncoderOptions
    {
        internal int? MaximumOutputBytes { get; init; }
        internal RoiOptions? Roi { get; init; }
        internal SubbandWeights? SubbandWeights { get; init; }
        internal bool AttributeSharedPayload => Roi != null;

        internal void Validate(BlkImgDataSrc source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (MaximumOutputBytes is null or <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumOutputBytes));
            if (source.NumComps != 3 || source.ImgWidth <= 0 || source.ImgHeight <= 0 ||
                (long)source.ImgWidth * source.ImgHeight > int.MaxValue || source.ImgULX != 0 || source.ImgULY != 0)
                throw new ArgumentException("Balanced encoding requires a supported RGB8 source.", nameof(source));
            for (var component = 0; component < 3; component++)
                if (source.getNomRangeBits(component) != 8 || source.getCompSubsX(component) != 1 ||
                    source.getCompSubsY(component) != 1 || source.getCompImgWidth(component) != source.ImgWidth ||
                    source.getCompImgHeight(component) != source.ImgHeight)
                    throw new ArgumentException("Balanced encoding requires three full-grid 8-bit components.", nameof(source));
            Roi?.ValidateForImage(source.ImgWidth, source.ImgHeight, 5);
            if (AttributeSharedPayload && ((long)source.ImgWidth * source.ImgHeight > 4L * 1024 * 1024 ||
                Math.Max(source.ImgWidth, source.ImgHeight) > 8192))
                throw new NotSupportedException("Synthesis-energy attribution supports at most 4,194,304 pixels and 8,192 samples per axis.");
        }

        internal void ValidateQuantizer(Quantizer quantizer)
        {
            if (quantizer == null) throw new ArgumentNullException(nameof(quantizer));
            var maximumMagnitude = Roi == null ? 31 : 15;
            var tileCount = quantizer.getNumTiles();
            quantizer.setTile(0, 0);
            try
            {
                for (var tile = 0; tile < tileCount; tile++)
                {
                    for (var component = 0; component < quantizer.NumComps; component++)
                    {
                        var magnitude = quantizer.getMaxMagBits(component);
                        if (magnitude < 1 || magnitude > maximumMagnitude)
                            throw new NotSupportedException(Roi == null
                                ? "Quantization exceeds the encoder's 31-bit magnitude arithmetic."
                                : "ROI Maxshift requires at most 15 quantized magnitude bits in this implementation; increase the quantization step or reduce source precision.");
                    }
                    if (tile + 1 < tileCount) quantizer.nextTile();
                }
            }
            finally { quantizer.setTile(0, 0); }
        }

        internal ParameterList ToParameterList()
        {
            var parameters = new ParameterList
            {
                ["file_format"] = "on", ["lossless"] = "off", ["Wlev"] = "5", ["Cblksiz"] = "64 64"
            };
            if (Roi != null)
            {
                parameters["Aptype"] = "layer";
                parameters["Rstart_level"] = "4";
                parameters["Ralign"] = "off";
            }
            return parameters;
        }
    }
}
