using System;
using System.Collections.Generic;
using CoreJ2K.FaceOFFx.ImageSharp;
using CoreJ2K.FaceOFFx.j2k.encoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CoreJ2K.FaceOFFx;

/// <summary>The fixed FaceOFFx RGB8 balanced JP2 encoder.</summary>
internal static class BalancedEncoder
{
    private static readonly SubbandWeights LumaWeights = CreateLumaWeights();

    /// <summary>Encodes within a complete JP2 byte cap while preserving caller-owned pixels.</summary>
    internal static EncoderResult Encode(Image<Rgb24> image, int maximumOutputBytes, RoiRegion faceRegion = null)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (maximumOutputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        return EncodingPipeline.Encode(new ImgReaderImageSharp(image), new EncoderOptions
        {
            MaximumOutputBytes = maximumOutputBytes,
            SubbandWeights = LumaWeights,
            Roi = faceRegion == null ? null : new RoiOptions { Regions = new[] { faceRegion } }
        });
    }

    private static SubbandWeights CreateLumaWeights()
    {
        var entries = new List<SubbandWeight> { new SubbandWeight(0, 0, WaveletSubband.LL, 1.25) };
        for (var resolution = 1; resolution <= 5; resolution++)
            foreach (var band in new[] { WaveletSubband.LH, WaveletSubband.HL, WaveletSubband.HH })
                entries.Add(new SubbandWeight(0, resolution, band, 1.25));
        return new SubbandWeights(entries);
    }
}
