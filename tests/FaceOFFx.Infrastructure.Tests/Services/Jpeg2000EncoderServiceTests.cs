using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using AwesomeAssertions;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
public class Jpeg2000EncoderServiceTests
{
    private readonly Jpeg2000EncoderService _encoder = new(NullLogger<Jpeg2000EncoderService>.Instance);

    [TestCase(2_000)]
    [TestCase(5_000)]
    [TestCase(11_820)]
    public void Encode_FixedBalancedRecipe_MeetsTheCompleteCapAndStoresRgb8(int cap)
    {
        using var image = CreateImage();
        var result = _encoder.Encode(image, CreateRoi(), new Jpeg2000EncodingOptions { MaximumOutputBytes = cap });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var data = result.Value.Data;
        data.Length.Should().BeLessThanOrEqualTo(cap);
        var siz = FindMarker(data, 0x51);
        var cod = FindMarker(data, 0x52);
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(siz + 38, 2)).Should().Be(3);
        for (var component = 0; component < 3; component++)
            data.AsSpan(siz + 40 + component * 3, 3).ToArray().Should().Equal(7, 1, 1);
        data[cod + 8].Should().Be(1);
        data[cod + 9].Should().Be(5);
        data[cod + 10].Should().Be(4);
        data[cod + 11].Should().Be(4);
        data[cod + 13].Should().Be(0);
        FindMarker(data, 0x5e).Should().BeGreaterThan(0);
        result.Value.RoiPixelCount.Should().Be(8_000);
        image[0, 0].A.Should().Be(255);
    }

    [Test]
    public void Encode_FixedByteCap_IsCultureIndependent()
    {
        using var image = CreateImage();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = _encoder.Encode(image, CreateRoi(), new Jpeg2000EncodingOptions { MaximumOutputBytes = 5_000 });
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = _encoder.Encode(image, CreateRoi(), new Jpeg2000EncodingOptions { MaximumOutputBytes = 5_000 });
            english.IsSuccess.Should().BeTrue();
            german.IsSuccess.Should().BeTrue();
            german.Value.Data.Should().Equal(english.Value.Data);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public void Encode_FixedMask_ReconcilesAllEvidenceAndPreservesCallerPixels()
    {
        using var image = CreateImage();
        var before = new Rgba32[160 * 200];
        image.CopyPixelDataTo(before);
        var pixels = new byte[160 * 200];
        for (var y = 50; y < 150; y++)
        for (var x = 40; x < 120; x++) pixels[y * 160 + x] = 1;
        var roi = CreateRoi() with { Mask = FacialRoiMask.FromBytes(160, 200, pixels) };
        var result = _encoder.Encode(image, roi, new Jpeg2000EncodingOptions { MaximumOutputBytes = 5_000 });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var payload = result.Value.PayloadTelemetry!;
        var attribution = result.Value.SharedPayloadAttribution!;
        payload.TotalOutputBytes.Should().Be(result.Value.Data.Length);
        payload.Subbands.Sum(row => row.PayloadBytes).Should().Be(payload.PacketBodyBytes);
        (payload.MainAndTileHeaderBytes + payload.PacketHeaderBytes + payload.PacketBodyBytes +
            payload.EndOfCodestreamBytes + payload.ContainerBytes).Should().Be(result.Value.Data.Length);
        attribution.PacketBodyBytes.Should().Be(payload.PacketBodyBytes);
        (attribution.AttributedFacePayloadBytes + attribution.OutsidePayloadBytes).Should().Be(payload.PacketBodyBytes);
        attribution.RoiPixelCount.Should().Be(8_000);
        attribution.AttributedFacePayloadBytes.Should().BeGreaterThan(0);
        result.Value.RoiMaskSha256.Should().Be(Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant());
        payload.WholeBandPromotedBytes.Should().BeGreaterThan(0);
        result.Value.AllocationCandidateCount.Should().BeGreaterThan(0);
        result.Value.AllocationSimulationCount.Should().BeGreaterThan(0);
        var after = new Rgba32[before.Length];
        image.CopyPixelDataTo(after);
        after.Should().Equal(before);
        roi.Mask!.ToArray().Should().Equal(pixels);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(80)]
    public void Encode_InsufficientOrInvalidCap_ReturnsFailure(int cap)
    {
        using var image = CreateImage();
        _encoder.Encode(image, CreateRoi(), new Jpeg2000EncodingOptions { MaximumOutputBytes = cap }).IsFailure.Should().BeTrue();
        image[0, 0].A.Should().Be(255);
    }

    [Test]
    public void Encode_RepeatedRequests_AreDeterministic()
    {
        using var image = CreateImage();
        var options = new Jpeg2000EncodingOptions { MaximumOutputBytes = 5_000 };
        var first = _encoder.Encode(image, CreateRoi(), options);
        var second = _encoder.Encode(image, CreateRoi(), options);
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Data.Should().Equal(first.Value.Data);
        second.Value.SharedPayloadAttribution!.FacePayloadByteEstimate.Should().Be(first.Value.SharedPayloadAttribution!.FacePayloadByteEstimate);
    }

    private static FacialRoiSet CreateRoi() => new(new RoiRegion(
        "Face", 3, new RoiBoundingBox(40, 50, 80, 100), Enumerable.Range(0, 68).ToArray()));

    private static Image<Rgba32> CreateImage()
    {
        var image = new Image<Rgba32>(160, 200);
        var random = new Random(719);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        });
        return image;
    }

    private static int FindMarker(byte[] data, byte marker)
    {
        for (var index = 0; index < data.Length - 1; index++)
            if (data[index] == 255 && data[index + 1] == marker) return index;
        throw new AssertionException($"Missing marker {marker:x2}.");
    }
}
