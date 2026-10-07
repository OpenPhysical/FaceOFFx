using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
public class Jpeg2000RegionalCapacityTests
{
    [Test]
    public void BalancedEncodingPreservesTheFixedRegionWhenMeasuredCreditExceedsTheReviewLimit()
    {
        using var image = new Image<Rgba32>(160, 200, new Rgba32(60, 100, 180, 255));
        var pixels = Enumerable.Repeat((byte)1, image.Width * image.Height).ToArray();
        var mask = FacialRoiMask.FromBytes(image.Width, image.Height, pixels);
        var roi = new FacialRoiSet(new RoiRegion("Fixed region", 3,
            new RoiBoundingBox(0, 0, image.Width, image.Height), Enumerable.Range(0, 68).ToArray()))
        {
            Mask = mask
        };
        var required = checked((int)((3 * mask.PixelCount + 23) / 24));
        var encoder = new Jpeg2000EncoderService(NullLogger<Jpeg2000EncoderService>.Instance);
        var result = encoder.Encode(image, roi, new Jpeg2000EncodingOptions
        {
            MaximumOutputBytes = required - 1
        });
        Assert.Multiple(() =>
        {
            Assert.That(required, Is.EqualTo(4_000));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.SharedPayloadAttribution!.AttributedFacePayloadBytes, Is.LessThan(required));
            Assert.That(roi.Mask, Is.SameAs(mask));
            Assert.That(mask.PixelCount, Is.EqualTo(32_000));
            Assert.That(mask.ToArray(), Is.EqualTo(pixels));
            Assert.That(image[0, 0], Is.EqualTo(new Rgba32(60, 100, 180, 255)));
            Assert.That(image[159, 199], Is.EqualTo(new Rgba32(60, 100, 180, 255)));
        });
    }
}
