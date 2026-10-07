using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
public class PivSourceColorServiceTests
{
    [Test]
    public void UntaggedSamplesRecordTheSrgbAssumptionAndPreserveTheSource()
    {
        using var image = Source();
        var before = Pixels(image);
        var declared = PivSourceColorService.Prepare(image);
        Assert.Multiple(() =>
        {
            Assert.That(declared.IsSuccess, Is.True);
            Assert.That(declared.Value.Status, Is.EqualTo(PivSourceColorStatus.AssumedSrgb));
            Assert.That(declared.Value.PixelsConverted, Is.False);
            Assert.That(declared.Value.EmbeddedProfileSha256, Is.Null);
            Assert.That(declared.Value.VerificationRequirements, Has.Count.EqualTo(1));
            Assert.That(Pixels(image), Is.EqualTo(before));
            Assert.That(image.Metadata.IccProfile, Is.Null);
            Assert.That(image.Metadata.ExifProfile, Is.Null);
        });
    }

    [TestCase((ushort)1, true)]
    [TestCase((ushort)65535, true)]
    public void ExifColorSpaceDeclaresSrgbOnlyWhenItsValueIsOne(ushort colorSpace, bool accepted)
    {
        using var image = Source();
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.ColorSpace, colorSpace);
        var exif = image.Metadata.ExifProfile;
        var beforeExif = exif.ToByteArray();
        var beforePixels = Pixels(image);
        var result = PivSourceColorService.Prepare(image);
        Assert.That(result.IsSuccess, Is.EqualTo(accepted));
        Assert.That(result.Value.Status, Is.EqualTo(colorSpace == 1 ? PivSourceColorStatus.ExifDeclaredSrgb : PivSourceColorStatus.AssumedSrgb));
        Assert.That(image.Metadata.ExifProfile, Is.SameAs(exif));
        Assert.That(image.Metadata.ExifProfile.ToByteArray(), Is.EqualTo(beforeExif));
        Assert.That(Pixels(image), Is.EqualTo(beforePixels));
    }

    [Test]
    public void EmbeddedSrgbUsesNumericColorantsAndCurvesAndPreservesSamples()
    {
        var fixture = Fixture("lcms-srgb-v4");
        using var image = Source();
        image.Metadata.IccProfile = new IccProfile(fixture.Profile);
        var profile = image.Metadata.IccProfile;
        var before = Pixels(image);
        var result = PivSourceColorService.Prepare(image);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : "");
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Status, Is.EqualTo(PivSourceColorStatus.EmbeddedSrgb));
            Assert.That(result.Value.PixelsConverted, Is.False);
            Assert.That(result.Value.EmbeddedProfileSha256, Is.EqualTo(fixture.Digest));
            Assert.That(image.Metadata.IccProfile, Is.SameAs(profile));
            Assert.That(profile.ToByteArray(), Is.EqualTo(fixture.Profile));
            Assert.That(Pixels(image), Is.EqualTo(before));
        });
    }

    [TestCase("synthetic-adobe-primaries-v2")]
    [TestCase("synthetic-adobe-primaries-v4")]
    public void MatrixConversionMatchesIndependentLcmsRgb8Vectors(string name)
    {
        var fixture = Fixture(name);
        using var image = new Image<Rgba32>(fixture.Input.Length / 3, 1);
        for (var index = 0; index < image.Width; index++)
            image[index, 0] = new Rgba32(fixture.Input[3 * index], fixture.Input[3 * index + 1], fixture.Input[3 * index + 2], (byte)(index % 256));
        image.Metadata.IccProfile = new IccProfile(fixture.Profile);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "Synthetic LCMS test");
        var result = PivSourceColorService.Prepare(image);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : "");
        for (var index = 0; index < image.Width; index++)
        {
            var pixel = image[index, 0];
            Assert.Multiple(() =>
            {
                Assert.That(pixel.R, Is.EqualTo(fixture.Output[3 * index]).Within(1), $"R sample {index}");
                Assert.That(pixel.G, Is.EqualTo(fixture.Output[3 * index + 1]).Within(1), $"G sample {index}");
                Assert.That(pixel.B, Is.EqualTo(fixture.Output[3 * index + 2]).Within(1), $"B sample {index}");
                Assert.That(pixel.A, Is.EqualTo((byte)(index % 256)));
            });
        }
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Status, Is.EqualTo(PivSourceColorStatus.ConvertedMatrixRgb));
            Assert.That(result.Value.Method, Does.Contain("relative-colorimetric"));
            Assert.That(result.Value.EmbeddedProfileSha256, Is.EqualTo(fixture.Digest));
            Assert.That(result.Value.PixelsConverted, Is.True);
            Assert.That(image.Metadata.IccProfile, Is.Null);
            Assert.That(image.Metadata.ExifProfile.TryGetValue(ExifTag.ColorSpace, out var colorSpace), Is.True);
            Assert.That(colorSpace!.Value, Is.EqualTo(1));
            Assert.That(image.Metadata.ExifProfile.TryGetValue(ExifTag.Artist, out var artist), Is.True);
            Assert.That(artist!.Value, Is.EqualTo("Synthetic LCMS test"));
        });
        var again = PivSourceColorService.Prepare(image);
        Assert.That(again.Value.Status, Is.EqualTo(PivSourceColorStatus.ExifDeclaredSrgb));
    }

    [Test]
    public void DescriptionTextCannotRelabelAWideGamutMatrixAsSrgb()
    {
        var fixture = Fixture("synthetic-adobe-primaries-v2");
        var offset = Tag(fixture.Profile, "desc").Offset;
        Encoding.ASCII.GetBytes("sRGB").CopyTo(fixture.Profile, offset + 12);
        using var image = Source();
        image.Metadata.IccProfile = new IccProfile(fixture.Profile);
        var result = PivSourceColorService.Prepare(image);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Status, Is.EqualTo(PivSourceColorStatus.ConvertedMatrixRgb));
    }

    [TestCase("LUT")]
    [TestCase("CMYK")]
    [TestCase("Lab")]
    [TestCase("DeviceLink")]
    [TestCase("D65Matrix")]
    [TestCase("MissingCurve")]
    [TestCase("DuplicateTag")]
    [TestCase("PartialOverlap")]
    [TestCase("TagCount")]
    [TestCase("TagOffset")]
    [TestCase("TagLength")]
    [TestCase("DeclaredSize")]
    [TestCase("Reserved")]
    [TestCase("ZeroGamma")]
    [TestCase("CurveCount")]
    [TestCase("ParametricType")]
    [TestCase("NegativePowerDomain")]
    public void RejectedProfilesPreserveAllSamplesAndMetadata(string defect)
    {
        var bytes = Fixture(defect is "ParametricType" or "NegativePowerDomain" ? "lcms-srgb-v4" : "synthetic-adobe-primaries-v2").Profile;
        var redCurve = Tag(bytes, "rTRC");
        switch (defect)
        {
            case "LUT": SetSignature(bytes, Tag(bytes, "chrm").Table, "A2B0"); break;
            case "CMYK": SetSignature(bytes, 16, "CMYK"); break;
            case "Lab": SetSignature(bytes, 20, "Lab "); break;
            case "DeviceLink": SetSignature(bytes, 12, "link"); break;
            case "D65Matrix": SetFixed(bytes, Tag(bytes, "bXYZ").Offset + 16, 1.0); break;
            case "MissingCurve": SetSignature(bytes, redCurve.Table, "xxxx"); break;
            case "DuplicateTag": SetSignature(bytes, redCurve.Table, "rXYZ"); break;
            case "PartialOverlap": SetU32(bytes, redCurve.Table + 4, (uint)Tag(bytes, "gXYZ").Offset + 4); break;
            case "TagCount": SetU32(bytes, 128, uint.MaxValue); break;
            case "TagOffset": SetU32(bytes, redCurve.Table + 4, uint.MaxValue); break;
            case "TagLength": SetU32(bytes, redCurve.Table + 8, uint.MaxValue); break;
            case "DeclaredSize": SetU32(bytes, 0, (uint)bytes.Length + 1); break;
            case "Reserved": bytes[100] = 1; break;
            case "ZeroGamma": bytes[redCurve.Offset + 12] = bytes[redCurve.Offset + 13] = 0; break;
            case "CurveCount": SetU32(bytes, redCurve.Offset + 8, uint.MaxValue); break;
            case "ParametricType": bytes[redCurve.Offset + 8] = 255; break;
            case "NegativePowerDomain": SetFixed(bytes, redCurve.Offset + 20, -1); break;
        }
        AssertRejectedPreserved(bytes);
    }

    [Test]
    public void EmptyTruncatedAndOversizedProfilesFailWithinStorageBounds()
    {
        AssertRejectedPreserved([]);
        AssertRejectedPreserved([1, 2, 3]);
        var oversized = new byte[1_048_580];
        SetU32(oversized, 0, (uint)oversized.Length);
        AssertRejectedPreserved(oversized);
        var forged = Fixture("lcms-srgb-v4").Profile;
        Array.Resize(ref forged, 1_048_580);
        AssertRejectedPreserved(forged);
    }

    [Test]
    public void AnimatedSourcesRequireAStillImageAndPreserveBothFrames()
    {
        using var image = Source();
        image.Frames.AddFrame(image.Frames.RootFrame);
        var first = Pixels(image);
        var result = PivSourceColorService.Prepare(image);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(Pixels(image), Is.EqualTo(first));
        Assert.That(image.Frames.Count, Is.EqualTo(2));
    }

    [Test]
    public void NullImageHasAnExplicitArgumentFailure() =>
        Assert.Throws<ArgumentNullException>(() => PivSourceColorService.Prepare(null!));

    private static void AssertRejectedPreserved(byte[] bytes)
    {
        using var image = Source();
        image.Metadata.IccProfile = new IccProfile(bytes);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.ColorSpace, (ushort)1);
        image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "Preserve on failure");
        var icc = image.Metadata.IccProfile;
        var exif = image.Metadata.ExifProfile;
        var beforeExif = exif.ToByteArray();
        var before = Pixels(image);
        var result = PivSourceColorService.Prepare(image);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(Pixels(image), Is.EqualTo(before));
            Assert.That(image.Metadata.IccProfile, Is.SameAs(icc));
            Assert.That(icc.ToByteArray(), Is.EqualTo(bytes));
            Assert.That(image.Metadata.ExifProfile, Is.SameAs(exif));
            Assert.That(exif.ToByteArray(), Is.EqualTo(beforeExif));
        });
    }

    private static Image<Rgba32> Source()
    {
        var image = new Image<Rgba32>(3, 2);
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 3; x++) image[x, y] = new Rgba32((byte)(70 * x), (byte)(100 * y), (byte)(40 + 20 * x), (byte)(50 + x));
        return image;
    }

    private static byte[] Pixels(Image<Rgba32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static (byte[] Profile, byte[] Input, byte[] Output, string Digest) Fixture(string name)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "SourceColor", "lcms-matrix-vectors.json")));
        var entry = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);
        var profile = Convert.FromBase64String(entry.GetProperty("profileBase64").GetString()!);
        var digest = Convert.ToHexString(SHA256.HashData(profile)).ToLowerInvariant();
        Assert.That(digest, Is.EqualTo(entry.GetProperty("profileSha256").GetString()));
        return (profile, Convert.FromBase64String(fixture.RootElement.GetProperty("inputRgbBase64").GetString()!),
            Convert.FromBase64String(entry.GetProperty("outputRgbBase64").GetString()!), digest);
    }

    private static (int Offset, int Table) Tag(byte[] bytes, string signature)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(128, 4));
        for (var index = 0; index < count; index++)
        {
            var table = 132 + 12 * index;
            if (Encoding.ASCII.GetString(bytes, table, 4) == signature)
                return ((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(table + 4, 4)), table);
        }
        throw new InvalidOperationException(signature);
    }

    private static void SetSignature(byte[] bytes, int offset, string signature) => Encoding.ASCII.GetBytes(signature).CopyTo(bytes, offset);
    private static void SetU32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    private static void SetFixed(byte[] bytes, int offset, double value) => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), (int)Math.Round(value * 65536));
}
