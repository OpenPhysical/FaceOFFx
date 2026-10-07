using System.Buffers.Binary;
using System.Globalization;
using CoreJ2K.FaceOFFx.j2k.encoder;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CoreJ2K.FaceOFFx.Tests;

[TestFixture]
public class BalancedEncoderTests
{
    [Test]
    public void MedicalBalancedEncodingMatchesTheTestedControlExactly()
    {
        using var image = Image.Load<Rgb24>(Fixture("medical-crop.png"));
        var mask = ReadMedicalMask();
        var result = BalancedEncoder.Encode(image, 11_820, RoiRegion.FromMask(mask));
        Assert.Multiple(() =>
        {
            Assert.That(result.Data, Is.EqualTo(File.ReadAllBytes(Fixture("medical-balanced.jp2"))));
            Assert.That(result.Data.Length, Is.EqualTo(11_771));
            Assert.That(result.RoiPixelCount, Is.EqualTo(60_318));
            Assert.That(result.SharedPayloadAttribution!.AttributedFacePayloadBytes, Is.EqualTo(3_383));
            Assert.That(result.PayloadTelemetry!.TotalOutputBytes, Is.EqualTo(result.Data.Length));
            Assert.That(result.SharedPayloadAttribution.PacketBodyBytes, Is.EqualTo(result.PayloadTelemetry.PacketBodyBytes));
        });
        AssertFixedCodestream(result.Data);
    }

    [TestCase(161, 203)]
    [TestCase(64, 64)]
    public void FixedRecipePreservesPixelsAndReturnsMeasuredEvidence(int width, int height)
    {
        using var image = CreateImage(width, height);
        var before = new Rgb24[width * height];
        image.CopyPixelDataTo(before);
        var mask = RoiMask.FromBytes(width, height, Enumerable.Repeat((byte)1, width * height).ToArray());
        var result = BalancedEncoder.Encode(image, 5_000, RoiRegion.FromMask(mask));
        var after = new Rgb24[before.Length];
        image.CopyPixelDataTo(after);
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(before));
            Assert.That(result.Data.Length, Is.LessThanOrEqualTo(5_000));
            Assert.That(result.PayloadTelemetry!.TotalOutputBytes, Is.EqualTo(result.Data.Length));
            Assert.That(result.SharedPayloadAttribution!.MethodId, Is.EqualTo("synthesis-energy-decoder-effective-pass-v3"));
        });
        AssertFixedCodestream(result.Data);
    }

    [Test]
    public void FixedByteBudgetIsCultureIndependent()
    {
        using var image = CreateImage(160, 200);
        var region = RoiRegion.Rectangle(40, 50, 80, 100);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = BalancedEncoder.Encode(image, 5_000, region);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = BalancedEncoder.Encode(image, 5_000, region);
            Assert.That(german.Data, Is.EqualTo(english.Data));
            Assert.That(german.SharedPayloadAttribution!.FacePayloadByteEstimate,
                Is.EqualTo(english.SharedPayloadAttribution!.FacePayloadByteEstimate));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(80)]
    public void ImpossibleByteCapsFailClearly(int cap)
    {
        using var image = CreateImage(64, 64);
        Assert.That(() => BalancedEncoder.Encode(image, cap), Throws.InstanceOf<ArgumentException>());
        Assert.That(image.Width, Is.EqualTo(64));
    }

    [Test]
    public void VendorAssemblyContainsOnlyInternalTypesAndEncoderEntryPoints()
    {
        var assembly = typeof(BalancedEncoder).Assembly;
        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetExportedTypes(), Is.Empty);
            Assert.That(assembly.GetTypes().Any(type => type.Namespace?.Contains(".decoder", StringComparison.Ordinal) == true), Is.False);
            Assert.That(assembly.GetTypes().Any(type => type.Name is "ImgReaderPGM" or "CoefficientDistortionAccumulator" or "HeaderDecoder"), Is.False);
            Assert.That(typeof(EncoderOptions).GetProperties().Any(property => property.Name.Contains("Minimum", StringComparison.Ordinal)), Is.False);
        });
    }

    private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);

    private static RoiMask ReadMedicalMask()
    {
        var bytes = File.ReadAllBytes(Fixture("medical-face.pgm"));
        var offset = 0;
        for (var newline = 0; newline < 3; offset++) if (bytes[offset] == '\n') newline++;
        return RoiMask.FromBytes(480, 640, bytes.AsSpan(offset));
    }

    private static Image<Rgb24> CreateImage(int width, int height)
    {
        var image = new Image<Rgb24>(width, height);
        var random = new Random(719);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++) row[x] = new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        });
        return image;
    }

    private static void AssertFixedCodestream(byte[] data)
    {
        var siz = FindMarker(data, 0x51);
        var cod = FindMarker(data, 0x52);
        Assert.Multiple(() =>
        {
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(siz + 38, 2)), Is.EqualTo(3));
            for (var component = 0; component < 3; component++)
                Assert.That(data.AsSpan(siz + 40 + component * 3, 3).ToArray(), Is.EqualTo(new byte[] { 7, 1, 1 }));
            Assert.That(data[cod + 8], Is.EqualTo(1), "ICT enabled");
            Assert.That(data[cod + 9], Is.EqualTo(5), "five decompositions");
            Assert.That(data[cod + 10], Is.EqualTo(4), "64-pixel block width");
            Assert.That(data[cod + 11], Is.EqualTo(4), "64-pixel block height");
            Assert.That(data[cod + 13], Is.EqualTo(0), "irreversible 9/7 transform");
        });
    }

    private static int FindMarker(byte[] data, byte marker)
    {
        for (var index = 0; index < data.Length - 1; index++)
            if (data[index] == 255 && data[index + 1] == marker) return index;
        throw new AssertionException($"Missing marker {marker:x2}.");
    }
}
